using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Observability;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Acp.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;
using DomainSessionMode = SalmonEgg.Domain.Models.Session.SessionMode;
using ProtocolSessionMode = SalmonEgg.Acp.Protocol.SessionMode;
using SalmonEgg.Acp.Client;

namespace SalmonEgg.Application.Services.Chat
{
    public class ChatService : IChatService
    {
        private readonly IAcpClient _acpClient;
        private readonly IErrorLogger _errorLogger;
        private readonly ISessionManager _sessionManager;
        // 保护下列四个共享可变态的所有读写:pump 线程(传输读线程续体)与请求-响应续体
        // (线程池)并发触碰它们。锁内只做同步内存读写与 HashSet 操作,绝不跨 await、
        // 绝不在锁内做协议 I/O 或调用可能重入的 _sessionManager 路径。
        private readonly object _stateGate = new();
        private readonly HashSet<string> _configAuthoritativeSessionIds = new(StringComparer.Ordinal);
        private string? _currentSessionId;
        private Plan? _currentPlan;
        private SessionModeState? _currentMode;
        private Task _sessionUpdatePump = Task.CompletedTask;
        private bool _disposed;

        public string? CurrentSessionId
        {
            get { lock (_stateGate) { return _currentSessionId; } }
        }

        public bool IsInitialized => _acpClient.IsInitialized;
        public bool IsConnected => _acpClient.IsConnected;
        public AgentInfo? AgentInfo => _acpClient.AgentInfo;
        public AgentCapabilities? AgentCapabilities => _acpClient.AgentCapabilities;

        // 返回快照而非 live List:pump 会并发向同一会话追加历史,直接把内部 List
        // 交给 UI 枚举会触发并发修改异常。Session.SnapshotHistory 在会话自己的锁下拷贝。
        public IReadOnlyList<SessionUpdateEntry> SessionHistory
        {
            get
            {
                string? sessionId;
                lock (_stateGate)
                {
                    sessionId = _currentSessionId;
                }

                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return Array.Empty<SessionUpdateEntry>();
                }

                return _sessionManager.GetSession(sessionId)?.SnapshotHistory()
                    ?? Array.Empty<SessionUpdateEntry>();
            }
        }

        public Plan? CurrentPlan
        {
            get { lock (_stateGate) { return _currentPlan; } }
        }

        public SessionModeState? CurrentMode
        {
            get { lock (_stateGate) { return _currentMode; } }
        }

        public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived;
        public event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived;
        public event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived;
        public event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived;
        public event EventHandler<TerminalStateChangedEventArgs>? TerminalStateChangedReceived;
        public event EventHandler<AskUserRequestEventArgs>? AskUserRequestReceived;
        public event EventHandler<string>? ErrorOccurred;

        public ChatService(IAcpClient acpClient, IErrorLogger errorLogger, ISessionManager sessionManager)
        {
            _acpClient = acpClient ?? throw new ArgumentNullException(nameof(acpClient));
            _errorLogger = errorLogger ?? throw new ArgumentNullException(nameof(errorLogger));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));

            _acpClient.SessionUpdateReceived += OnSessionUpdateReceived;
            _acpClient.PermissionRequestReceived += OnPermissionRequestReceived;
            _acpClient.FileSystemRequestReceived += OnFileSystemRequestReceived;
            _acpClient.TerminalRequestReceived += OnTerminalRequestReceived;
            _acpClient.TerminalStateChangedReceived += OnTerminalStateChangedReceived;
            _acpClient.AskUserRequestReceived += OnAskUserRequestReceived;
            _acpClient.ErrorOccurred += OnErrorOccurred;
        }

        private Session? GetSession(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return null;

            return _sessionManager.GetSession(sessionId);
        }

        private async Task<Session> GetOrCreateSessionAsync(string sessionId, string cwd)
        {
            var existing = _sessionManager.GetSession(sessionId);
            if (existing != null)
                return existing;

            return await _sessionManager.CreateSessionAsync(sessionId, cwd).ConfigureAwait(false);
        }

        private void OnSessionUpdateReceived(object? sender, SessionUpdateEventArgs e)
        {
            // 事件由传输读线程按到达序同步触发;若直接用 async void,session 首建等待点之后的
            // 续体可能与后续事件交错,打乱 history 追加与事件转发次序。链式管道保证严格串行,
            // 且链头读改写只发生在同一触发线程上,无需加锁。
            _sessionUpdatePump = ProcessSessionUpdateSequentiallyAsync(_sessionUpdatePump, e);
        }

        private async Task ProcessSessionUpdateSequentiallyAsync(Task previous, SessionUpdateEventArgs e)
        {
            try
            {
                await previous.ConfigureAwait(false);
            }
            catch
            {
                // 每个环节自行记录失败;链头只负责排序,前环故障不得毒化后续更新。
            }

            try
            {
                if (e.Update != null)
                {
                    // A session id only exists because we established it through session/new, load or
                    // resume, and each of those carries the cwd. An update for an id we do not track is
                    // therefore either late (the session was closed or rolled back) or the peer is out
                    // of step; materialising a session from it would invent one with no cwd, which is
                    // not a state a session can legitimately be in. Record and skip local tracking.
                    // Forwarding below is deliberately left intact so subscribers still observe it.
                    var session = _sessionManager.GetSession(e.SessionId);
                    if (session is null)
                    {
                        _errorLogger.LogError(new ErrorLogEntry(
                            "Session update received for an untracked session",
                            "The update was not recorded locally because the session is not established.",
                            ErrorSeverity.Warning,
                            nameof(OnSessionUpdateReceived),
                            e.SessionId));
                    }
                    else
                    {
                        session.AppendHistory(CreateSessionUpdateEntry(e.Update, e.SessionId));
                    }

                    // CRITICAL PATH: Syncing Agent's internal state (Plan, Mode) with our local variables.
                    // This allows the ViewModel to access the latest state without parsing history.
                    // 共享态的判定与写入必须在 _stateGate 内:pump 与请求路径续体真并发触碰同一字段。
                    lock (_stateGate)
                    {
                        switch (e.Update)
                        {
                            case PlanUpdate planUpdate:
                                if (planUpdate.Entries != null
                                    && string.Equals(_currentSessionId, e.SessionId, StringComparison.Ordinal))
                                {
                                    _currentPlan = new Plan { Entries = planUpdate.Entries };
                                }
                                break;
                            case CurrentModeUpdate modeChange:
                                if (!string.IsNullOrEmpty(modeChange.ModeId)
                                    && !_configAuthoritativeSessionIds.Contains(e.SessionId))
                                {
                                    ApplyCurrentModeId(e.SessionId, modeChange.ModeId);
                                }
                                break;
                            case ConfigOptionUpdate configOption:
                                if (configOption.ConfigOptions is not null)
                                {
                                    MarkConfigOptionsAuthoritative(e.SessionId);
                                }
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Session tracking failures must not break event forwarding, but they may not be silent either.
                _errorLogger.LogError(new ErrorLogEntry(
                    "Session update tracking failed",
                    ex.Message,
                    ErrorSeverity.Warning,
                    nameof(OnSessionUpdateReceived),
                    e.SessionId,
                    ex));
            }

            try
            {
                SessionUpdateReceived?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                // 订阅者异常在旧 async void 形态下会击穿进程,这里落日志并阻断传播。
                _errorLogger.LogError(new ErrorLogEntry(
                    "Session update handler failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(OnSessionUpdateReceived),
                    e.SessionId,
                    ex));
            }
        }

        private void OnPermissionRequestReceived(object? sender, PermissionRequestEventArgs e)
        {
            PermissionRequestReceived?.Invoke(this, e);
        }

        private void OnFileSystemRequestReceived(object? sender, FileSystemRequestEventArgs e)
        {
            FileSystemRequestReceived?.Invoke(this, e);
        }

        private void OnErrorOccurred(object? sender, string error)
        {
            ErrorOccurred?.Invoke(this, error);
            var entry = new ErrorLogEntry(
                "Error occurred",
                error,
                ErrorSeverity.Error,
                nameof(OnErrorOccurred),
                _currentSessionId);
            _errorLogger.LogError(entry);
        }

        private void OnAskUserRequestReceived(object? sender, AskUserRequestEventArgs e)
        {
            AskUserRequestReceived?.Invoke(this, e);
        }

        private static SessionUpdateEntry CreateSessionUpdateEntry(SessionUpdate update, string sessionId)
        {
            var entry = new SessionUpdateEntry
            {
                Timestamp = DateTime.UtcNow,
                SessionUpdateType = GetSessionUpdateType(update)
            };

            switch (update)
            {
                case AgentMessageUpdate messageUpdate:
                    ApplyContentProjection(entry, messageUpdate.Content);
                    break;
                case UserMessageUpdate userMessageUpdate:
                    ApplyContentProjection(entry, userMessageUpdate.Content);
                    break;
                case AgentThoughtUpdate thoughtUpdate:
                    ApplyContentProjection(entry, thoughtUpdate.Content);
                    break;
                case ToolCallUpdate toolCallUpdate:
                    entry.ToolCallId = toolCallUpdate.ToolCallId;
                    entry.ToolCallKind = toolCallUpdate.Kind?.ToString();
                    entry.ToolCallStatus = toolCallUpdate.Status?.ToString();
                    entry.Title = toolCallUpdate.Title;
                    break;
                case ToolCallStatusUpdate toolCallStatusUpdate:
                    entry.ToolCallId = toolCallStatusUpdate.ToolCallId;
                    entry.ToolCallKind = toolCallStatusUpdate.Kind?.ToString();
                    entry.ToolCallStatus = toolCallStatusUpdate.Status?.ToString();
                    entry.Title = toolCallStatusUpdate.Title;
                    break;
                case PlanUpdate planUpdate:
                    entry.PlanEntries = planUpdate.Entries?
                        .Select(static planEntry => new SessionPlanHistoryEntry(
                            planEntry.Content,
                            planEntry.Status.ToString(),
                            planEntry.Priority.ToString()))
                        .ToList();
                    break;
                case CurrentModeUpdate modeChange:
                    entry.ModeId = modeChange.ModeId;
                    break;
                case SessionInfoUpdate sessionInfoUpdate:
                    entry.Title = sessionInfoUpdate.Title;
                    break;
                // Config options, available commands, and usage remain ACP wire concerns.
                // Domain history only keeps recovery-relevant projections.
            }

            return entry;
        }

        private static void ApplyContentProjection(SessionUpdateEntry entry, ContentBlock? content)
        {
            if (content is null)
            {
                return;
            }

            entry.ContentType = content.Type;
            if (content is TextContentBlock textBlock)
            {
                entry.TextContent = textBlock.Text;
            }
        }

        private static string GetSessionUpdateType(SessionUpdate update) =>
            update switch
            {
                AgentMessageUpdate => "agent_message_chunk",
                UserMessageUpdate => "user_message_chunk",
                AgentThoughtUpdate => "agent_thought_chunk",
                ToolCallUpdate => "tool_call",
                ToolCallStatusUpdate => "tool_call_update",
                PlanUpdate => "plan",
                CurrentModeUpdate => "current_mode_update",
                AvailableCommandsUpdate => "available_commands_update",
                ConfigOptionUpdate => "config_option_update",
                SessionInfoUpdate => "session_info_update",
                UsageUpdate => "usage_update",
                _ => "unknown"
            };

        // 以下 Apply*/Capture*/Restore* 系列约定:调用方必须已持 _stateGate。
        // 它们读改写 _currentMode/_currentSessionId/_configAuthoritativeSessionIds 并可能
        // 调用 Session 上的具名操作(走会话自己的独立锁,单向嵌套 _stateGate→Session 内部锁,
        // 会话从不回调本类,故无死锁)。
        private void ApplySessionResponseModeState(
            string sessionId,
            SessionModesState? modes,
            IReadOnlyList<ConfigOption>? configOptions)
        {
            if (configOptions is not null)
            {
                MarkConfigOptionsAuthoritative(sessionId);
                return;
            }

            _configAuthoritativeSessionIds.Remove(sessionId);
            ApplyModeState(sessionId, BuildModeState(modes));
        }

        private void ApplyCurrentModeId(string sessionId, string modeId)
        {
            var sessionMode = _sessionManager.GetSession(sessionId)?.SnapshotMode();
            var source = string.Equals(_currentSessionId, sessionId, StringComparison.Ordinal)
                ? _currentMode ?? sessionMode
                : sessionMode;
            var state = CloneModeState(source) ?? new SessionModeState();
            state.CurrentModeId = modeId;
            state.CurrentMode = state.GetModeById(modeId);
            ApplyModeState(sessionId, state);
        }

        private void MarkConfigOptionsAuthoritative(string sessionId)
        {
            _configAuthoritativeSessionIds.Add(sessionId);
            ApplyModeState(sessionId, null);
        }

        private void ApplyModeState(string sessionId, SessionModeState? state)
        {
            var projectedState = state ?? new SessionModeState();
            if (string.Equals(_currentSessionId, sessionId, StringComparison.Ordinal))
            {
                _currentMode = state;
            }

            // SetMode 自己存深拷贝,这里不必再克隆一次。
            _sessionManager.GetSession(sessionId)?.SetMode(projectedState);
        }

        private static SessionModeState? BuildModeState(SessionModesState? modes)
        {
            if (modes is null)
            {
                return null;
            }

            var state = new SessionModeState
            {
                CurrentModeId = modes.CurrentModeId ?? string.Empty,
                AvailableModes = modes.AvailableModes?
                    .Where(static mode => mode is not null)
                    .Select(static mode => new DomainSessionMode(
                        mode!.Id ?? string.Empty,
                        mode.Name ?? string.Empty,
                        mode.Description))
                    .ToList() ?? new List<DomainSessionMode>()
            };
            state.CurrentMode = state.GetModeById(state.CurrentModeId);
            return state;
        }

        private static SessionModeState? CloneModeState(SessionModeState? source)
            => source?.DeepCopy();

        private static Plan? ClonePlan(Plan? source)
        {
            if (source is null)
            {
                return null;
            }

            return new Plan
            {
                Entries = source.Entries
                    .Select(static entry => new PlanEntry(entry.Content, entry.Status, entry.Priority)
                    {
                        Meta = entry.Meta is null ? null : new Dictionary<string, object?>(entry.Meta)
                    })
                    .ToList(),
                Meta = source.Meta is null ? null : new Dictionary<string, object?>(source.Meta)
            };
        }

        private SessionSnapshot CaptureSessionSnapshot(string sessionId)
        {
            var session = _sessionManager.GetSession(sessionId);
            var configOptionsAuthoritative = _configAuthoritativeSessionIds.Contains(sessionId);
            if (session is null)
            {
                return new SessionSnapshot(
                    sessionId,
                    false,
                    configOptionsAuthoritative,
                    SessionState.Active,
                    null,
                    []);
            }

            return new SessionSnapshot(
                sessionId,
                true,
                configOptionsAuthoritative,
                session.State,
                session.SnapshotMode(),
                session.SnapshotHistory());
        }

        private void RestoreSessionSnapshot(SessionSnapshot snapshot)
        {
            if (!snapshot.Existed)
            {
                _sessionManager.RemoveSession(snapshot.SessionId);
                RestoreConfigAuthority(snapshot);
                return;
            }

            // 状态、模式、历史必须一次性回滚:分三次写入会让并发读者看到"改了一半"的会话。
            _sessionManager.GetSession(snapshot.SessionId)?.RestoreSnapshot(
                snapshot.State,
                snapshot.Mode,
                snapshot.History);
            RestoreConfigAuthority(snapshot);
        }

        private void RestoreConfigAuthority(SessionSnapshot snapshot)
        {
            if (snapshot.ConfigOptionsAuthoritative)
            {
                _configAuthoritativeSessionIds.Add(snapshot.SessionId);
                return;
            }

            _configAuthoritativeSessionIds.Remove(snapshot.SessionId);
        }

        private static List<ProtocolSessionMode> ToProtocolModes(SessionModeState state)
            => state.AvailableModes
                .Select(static mode => new ProtocolSessionMode
                {
                    Id = mode.Id,
                    Name = mode.Name,
                    Description = mode.Description
                })
                .ToList();

        private sealed class SessionSnapshot
        {
            public SessionSnapshot(
                string sessionId,
                bool existed,
                bool configOptionsAuthoritative,
                SessionState state,
                SessionModeState? mode,
                IReadOnlyList<SessionUpdateEntry> history)
            {
                SessionId = sessionId;
                Existed = existed;
                ConfigOptionsAuthoritative = configOptionsAuthoritative;
                State = state;
                Mode = mode;
                History = history;
            }

            public string SessionId { get; }

            public bool Existed { get; }

            public bool ConfigOptionsAuthoritative { get; }

            public SessionState State { get; }

            public SessionModeState? Mode { get; }

            public IReadOnlyList<SessionUpdateEntry> History { get; }
        }

        public async Task<InitializeResponse> InitializeAsync(InitializeParams @params)
        {
            using var activity = ApplicationActivitySources.ChatService.StartActivity(
                "chat.initialize",
                ActivityKind.Internal);
            try
            {
                var response = await _acpClient.InitializeAsync(@params);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return response;
            }
            catch (Exception ex)
            {
                activity?.SetErrorStatus(ex);
                activity?.RecordException(ex);
                var entry = new ErrorLogEntry(
                    "InitializeAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(InitializeAsync),
                    null,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params)
        {
            using var activity = ApplicationActivitySources.ChatService.StartActivity(
                "chat.session.create",
                ActivityKind.Internal);
            try
            {
                var response = await _acpClient.CreateSessionAsync(@params);

                if (!string.IsNullOrWhiteSpace(response.SessionId))
                {
                    await GetOrCreateSessionAsync(response.SessionId, @params.Cwd).ConfigureAwait(false);
                    _sessionManager.GetSession(response.SessionId)?.ResetForNewSession();
                }

                lock (_stateGate)
                {
                    _currentSessionId = response.SessionId;
                    _currentPlan = null;
                    _currentMode = null;
                    if (!string.IsNullOrWhiteSpace(response.SessionId))
                    {
                        ApplySessionResponseModeState(response.SessionId, response.Modes, response.ConfigOptions);
                    }
                }

                activity?.SetStatus(ActivityStatusCode.Ok);
                return response;
            }
            catch (Exception ex)
            {
                activity?.SetErrorStatus(ex);
                activity?.RecordException(ex);
                var entry = new ErrorLogEntry(
                    "CreateSessionAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(CreateSessionAsync),
                    _currentSessionId,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params)
            => LoadSessionAsync(@params, CancellationToken.None);

        public async Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken)
        {
            using var activity = ApplicationActivitySources.ChatService.StartActivity(
                "chat.session.load",
                ActivityKind.Internal);
            SessionSnapshot targetSnapshot;
            string? previousSessionId;
            Plan? previousPlan;
            SessionModeState? previousMode;
            lock (_stateGate)
            {
                previousSessionId = _currentSessionId;
                previousPlan = ClonePlan(_currentPlan);
                previousMode = CloneModeState(_currentMode);
                targetSnapshot = CaptureSessionSnapshot(@params.SessionId);

                // CRITICAL: We update _currentSessionId *before* LoadSessionAsync
                // because the loading process triggers Replay events, which must be
                // associated with the new session ID immediately.
                _currentSessionId = @params.SessionId;
                _currentPlan = null;
                _currentMode = null;
            }

            try
            {
                // The slot is created carrying this cwd, and a session's cwd never changes, so there is
                // nothing to re-assign here.
                _sessionManager.GetOrCreateTrackingSlot(@params.SessionId, @params.Cwd);

                // Clear history before loading to ensure we don't have duplicate entries
                // if the server replays the history during the load process.
                _sessionManager.GetSession(@params.SessionId)?.ClearHistory();

                var response = await _acpClient.LoadSessionAsync(@params, cancellationToken).ConfigureAwait(false);
                try
                {
                    _sessionManager.GetSession(@params.SessionId)?.SetState(SessionState.Active);
                    lock (_stateGate)
                    {
                        ApplySessionResponseModeState(@params.SessionId, response.Modes, response.ConfigOptions);
                    }
                }
                catch
                {
                    // Ignore session tracking failures
                }
                activity?.SetStatus(ActivityStatusCode.Ok);
                return response;
            }
            catch (OperationCanceledException)
            {
                RollBackAfterFailedRecovery(@params.SessionId, targetSnapshot, previousSessionId, previousPlan, previousMode);
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetErrorStatus(ex);
                activity?.RecordException(ex);
                RollBackAfterFailedRecovery(@params.SessionId, targetSnapshot, previousSessionId, previousPlan, previousMode);

                var entry = new ErrorLogEntry(
                    "LoadSessionAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(LoadSessionAsync),
                    @params.SessionId,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        // 失败回滚必须带 latest-intent 门控:同一 ChatService 上可能已有更新的 Load/Resume
        // 请求接管并写入了自己的 _currentSessionId。旧请求失败时只有在"当前指针仍指向本请求的
        // 会话"时才回滚全局指针,否则会砸掉新请求已建立的最新意图(硬约束 §8.1.2)。会话级
        // snapshot 恢复始终执行——它按 conversationId 隔离,只撤销本请求对目标会话造成的副作用。
        private void RollBackAfterFailedRecovery(
            string sessionId,
            SessionSnapshot targetSnapshot,
            string? previousSessionId,
            Plan? previousPlan,
            SessionModeState? previousMode)
        {
            lock (_stateGate)
            {
                RestoreSessionSnapshot(targetSnapshot);

                if (string.Equals(_currentSessionId, sessionId, StringComparison.Ordinal))
                {
                    _currentSessionId = previousSessionId;
                    _currentPlan = previousPlan;
                    _currentMode = previousMode;
                }
            }
        }

        public Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params)
            => ResumeSessionAsync(@params, CancellationToken.None);

        public async Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params, CancellationToken cancellationToken)
        {
            using var activity = ApplicationActivitySources.ChatService.StartActivity(
                "chat.session.resume",
                ActivityKind.Internal);
            SessionSnapshot targetSnapshot;
            string? previousSessionId;
            Plan? previousPlan;
            SessionModeState? previousMode;
            lock (_stateGate)
            {
                previousSessionId = _currentSessionId;
                previousPlan = ClonePlan(_currentPlan);
                previousMode = CloneModeState(_currentMode);
                targetSnapshot = CaptureSessionSnapshot(@params.SessionId);

                _currentSessionId = @params.SessionId;
                _currentPlan = null;
                _currentMode = null;
            }

            try
            {
                // The slot is created carrying this cwd, and a session's cwd never changes.
                _sessionManager.GetOrCreateTrackingSlot(@params.SessionId, @params.Cwd);

                var response = await _acpClient.ResumeSessionAsync(@params, cancellationToken).ConfigureAwait(false);
                _sessionManager.GetSession(@params.SessionId)?.SetState(SessionState.Active);
                lock (_stateGate)
                {
                    ApplySessionResponseModeState(@params.SessionId, response.Modes, response.ConfigOptions);
                }
                activity?.SetStatus(ActivityStatusCode.Ok);
                return response;
            }
            catch (OperationCanceledException)
            {
                RollBackAfterFailedRecovery(@params.SessionId, targetSnapshot, previousSessionId, previousPlan, previousMode);
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetErrorStatus(ex);
                activity?.RecordException(ex);
                RollBackAfterFailedRecovery(@params.SessionId, targetSnapshot, previousSessionId, previousPlan, previousMode);

                var entry = new ErrorLogEntry(
                    "ResumeSessionAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(ResumeSessionAsync),
                    @params.SessionId,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task<SessionCloseResponse> CloseSessionAsync(SessionCloseParams @params, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _acpClient.CloseSessionAsync(@params, cancellationToken).ConfigureAwait(false);
                _sessionManager.RemoveSession(@params.SessionId);

                lock (_stateGate)
                {
                    if (string.Equals(_currentSessionId, @params.SessionId, StringComparison.Ordinal))
                    {
                        _currentSessionId = null;
                        _currentPlan = null;
                        _currentMode = null;
                    }
                    _configAuthoritativeSessionIds.Remove(@params.SessionId);
                }

                return response;
            }
            catch (Exception ex)
            {
                var entry = new ErrorLogEntry(
                    "CloseSessionAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(CloseSessionAsync),
                    @params.SessionId,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task<SessionListResponse> ListSessionsAsync(SessionListParams? @params = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _acpClient.ListSessionsAsync(@params ?? new SessionListParams(), cancellationToken);
                return response;
            }
            catch (Exception ex)
            {
                var entry = new ErrorLogEntry(
                    "ListSessionsAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(ListSessionsAsync),
                    _currentSessionId,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default)
        {
            using var activity = ApplicationActivitySources.ChatService.StartActivity(
                "chat.session.prompt",
                ActivityKind.Internal);
            try
            {
                var response = await _acpClient.SendPromptAsync(@params, cancellationToken).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetErrorStatus(ex);
                activity?.RecordException(ex);
                var entry = new ErrorLogEntry(
                    "SendPromptAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(SendPromptAsync),
                    @params.SessionId,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params)
        {
            try
            {
                var response = await _acpClient.SetSessionModeAsync(@params);
                if (!string.IsNullOrEmpty(@params.ModeId))
                {
                    lock (_stateGate)
                    {
                        ApplyCurrentModeId(@params.SessionId, @params.ModeId);
                    }
                }
                return response;
            }
            catch (Exception ex)
            {
                var entry = new ErrorLogEntry(
                    "SetSessionModeAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(SetSessionModeAsync),
                    @params.SessionId,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params)
        {
            try
            {
                var response = await _acpClient.SetSessionConfigOptionAsync(@params);
                return response;
            }
            catch (Exception ex)
            {
                var entry = new ErrorLogEntry(
                    "SetSessionConfigOptionAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(SetSessionConfigOptionAsync),
                    @params.SessionId,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task CancelSessionAsync(SessionCancelParams @params)
        {
            try
            {
                await _acpClient.CancelSessionAsync(@params).ConfigureAwait(false);
                // Agent 已接受取消,这里只是把权威结果投影到本地容器,因此无条件写入;
                // 而 SessionManager.CancelSessionAsync 走 TryCancel,那里"能否取消"才是一次判定。
                _sessionManager.GetSession(@params.SessionId)?.SetState(SessionState.Cancelled);
            }
            catch (Exception ex)
            {
                var entry = new ErrorLogEntry(
                    "CancelSessionAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(CancelSessionAsync),
                    @params.SessionId,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _acpClient.AuthenticateAsync(@params, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var entry = new ErrorLogEntry(
                    "AuthenticateAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(AuthenticateAsync),
                    _currentSessionId,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null)
        {
            try
            {
                return await _acpClient.RespondToPermissionRequestAsync(messageId, outcome, optionId);
            }
            catch (Exception ex)
            {
                var entry = new ErrorLogEntry(
                    "RespondToPermissionRequestAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(RespondToPermissionRequestAsync),
                    null,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
        {
            try
            {
                return await _acpClient.RespondToFileSystemRequestAsync(messageId, success, content, message);
            }
            catch (Exception ex)
            {
                var entry = new ErrorLogEntry(
                    "RespondToFileSystemRequestAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(RespondToFileSystemRequestAsync),
                    null,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers)
        {
            try
            {
                return await _acpClient.RespondToAskUserRequestAsync(messageId, answers).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var entry = new ErrorLogEntry(
                    "RespondToAskUserRequestAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(RespondToAskUserRequestAsync),
                    null,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public async Task<bool> DisconnectAsync()
        {
            try
            {
                lock (_stateGate)
                {
                    _currentSessionId = null;
                    _currentPlan = null;
                    _currentMode = null;
                    _configAuthoritativeSessionIds.Clear();
                }

                return await _acpClient.DisconnectAsync();
            }
            catch (Exception ex)
            {
                var entry = new ErrorLogEntry(
                    "DisconnectAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(DisconnectAsync),
                    null,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public Task<List<SalmonEgg.Acp.Protocol.SessionMode>?> GetAvailableModesAsync()
        {
            try
            {
                lock (_stateGate)
                {
                    if (string.IsNullOrEmpty(_currentSessionId))
                    {
                        return Task.FromResult<List<SalmonEgg.Acp.Protocol.SessionMode>?>(null);
                    }

                    return Task.FromResult<List<SalmonEgg.Acp.Protocol.SessionMode>?>(
                        _currentMode is null ? null : ToProtocolModes(_currentMode));
                }
            }
            catch (Exception ex)
            {
                var entry = new ErrorLogEntry(
                    "GetAvailableModesAsync failed",
                    ex.Message,
                    ErrorSeverity.Error,
                    nameof(GetAvailableModesAsync),
                    _currentSessionId,
                    ex);
                _errorLogger.LogError(entry);
                throw;
            }
        }

        public void ClearHistory()
        {
            string? sessionId;
            lock (_stateGate)
            {
                sessionId = _currentSessionId;
                _currentPlan = null;
                _currentMode = null;
            }

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _sessionManager.GetSession(sessionId)?.ClearHistory();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _acpClient.SessionUpdateReceived -= OnSessionUpdateReceived;
            _acpClient.PermissionRequestReceived -= OnPermissionRequestReceived;
            _acpClient.FileSystemRequestReceived -= OnFileSystemRequestReceived;
            _acpClient.TerminalRequestReceived -= OnTerminalRequestReceived;
            _acpClient.TerminalStateChangedReceived -= OnTerminalStateChangedReceived;
            _acpClient.AskUserRequestReceived -= OnAskUserRequestReceived;
            _acpClient.ErrorOccurred -= OnErrorOccurred;

            // 本服务独占其 ACP 客户端（进而独占传输/进程/套接字/CTS），释放沿所有权链下传。
            // 优雅的协议级断连由显式 DisconnectAsync 负责，调用方已在 Dispose 前先行 await。
            try
            {
                _acpClient.Dispose();
            }
            catch (Exception ex)
            {
                // 清理路径的释放失败不得逃逸，否则会顶替真正的业务异常并挂死调用栈。
                _errorLogger.LogError(new ErrorLogEntry(
                    "CHAT_SERVICE_CLIENT_DISPOSE_FAILED",
                    "Failed to dispose ACP client during chat service disposal.",
                    ErrorSeverity.Warning,
                    nameof(Dispose),
                    exception: ex));
            }
        }

        private void OnTerminalRequestReceived(object? sender, TerminalRequestEventArgs e)
        {
            TerminalRequestReceived?.Invoke(this, e);
        }

        private void OnTerminalStateChangedReceived(object? sender, TerminalStateChangedEventArgs e)
        {
            TerminalStateChangedReceived?.Invoke(this, e);
        }
    }
}
