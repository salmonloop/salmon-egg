using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;
using DomainSessionMode = SalmonEgg.Domain.Models.Session.SessionMode;
using ProtocolSessionMode = SalmonEgg.Domain.Models.Protocol.SessionMode;

namespace SalmonEgg.Application.Services.Chat
{
    public class ChatService : IChatService
    {
        private readonly IAcpClient _acpClient;
        private readonly IErrorLogger _errorLogger;
        private readonly ISessionManager _sessionManager;
        private readonly HashSet<string> _configAuthoritativeSessionIds = new(StringComparer.Ordinal);
        private string? _currentSessionId;
        private Plan? _currentPlan;
        private SessionModeState? _currentMode;

        public string? CurrentSessionId => _currentSessionId;
        public bool IsInitialized => _acpClient.IsInitialized;
        public bool IsConnected => _acpClient.IsConnected;
        public AgentInfo? AgentInfo => _acpClient.AgentInfo;
        public AgentCapabilities? AgentCapabilities => _acpClient.AgentCapabilities;
        public IReadOnlyList<SessionUpdateEntry> SessionHistory =>
            (IReadOnlyList<SessionUpdateEntry>?)GetSession(_currentSessionId)?.History ?? Array.Empty<SessionUpdateEntry>();
        public Plan? CurrentPlan => _currentPlan;
        public SessionModeState? CurrentMode => _currentMode;

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

        private async Task<Session> GetOrCreateSessionAsync(string sessionId, string? cwd = null)
        {
            var existing = _sessionManager.GetSession(sessionId);
            if (existing != null)
                return existing;

            return await _sessionManager.CreateSessionAsync(sessionId, cwd).ConfigureAwait(false);
        }

        private async void OnSessionUpdateReceived(object? sender, SessionUpdateEventArgs e)
        {
            if (e.Update != null)
            {
                var entry = CreateSessionUpdateEntry(e.Update, e.SessionId);
                if (entry != null)
                {
                    try
                    {
                        var session = await GetOrCreateSessionAsync(e.SessionId).ConfigureAwait(false);
                        session.AddHistoryEntry(entry);
                    }
                    catch
                    {
                        // Ignore session tracking failures; UI will still receive SessionUpdateReceived.
                    }

                    // CRITICAL PATH: Syncing Agent's internal state (Plan, Mode) with our local variables.
                    // This allows the ViewModel to access the latest state without parsing history.
                    switch (e.Update)
                    {
                        case AgentMessageUpdate messageUpdate:
                            break;
                        case AgentThoughtUpdate thoughtUpdate:
                            break;
                        case ToolCallUpdate toolCallUpdate:
                            break;
                        case ToolCallStatusUpdate toolCallStatusUpdate:
                            break;
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
                        case ConfigUpdateUpdate configUpdate:
                            if (configUpdate.ConfigOptions is not null)
                            {
                                MarkConfigOptionsAuthoritative(e.SessionId);
                            }
                            break;
                    }
                }
            }

            SessionUpdateReceived?.Invoke(this, e);
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

        private SessionUpdateEntry? CreateSessionUpdateEntry(SessionUpdate update, string sessionId)
        {
            var entry = new SessionUpdateEntry
            {
                Timestamp = DateTime.UtcNow,
                SessionUpdateType = GetSessionUpdateType(update)
            };

            switch (update)
            {
                case AgentMessageUpdate messageUpdate:
                    entry.Content = messageUpdate.Content;
                    break;
                case AgentThoughtUpdate thoughtUpdate:
                    entry.Content = thoughtUpdate.Content;
                    break;
                case ToolCallUpdate toolCallUpdate:
                    entry.ToolCallId = toolCallUpdate.ToolCallId;
                    entry.Kind = toolCallUpdate.Kind;
                    entry.Status = toolCallUpdate.Status;
                    entry.Title = toolCallUpdate.Title;
                    break;
                case ToolCallStatusUpdate toolCallStatusUpdate:
                    entry.ToolCallId = toolCallStatusUpdate.ToolCallId;
                    entry.Kind = toolCallStatusUpdate.Kind;
                    entry.Status = toolCallStatusUpdate.Status;
                    entry.Title = toolCallStatusUpdate.Title;
                    break;
                case PlanUpdate planUpdate:
                    entry.Entries = planUpdate.Entries;
                    break;
                case CurrentModeUpdate modeChange:
                    entry.ModeId = modeChange.ModeId;
                    entry.Title = modeChange.Title;
                    break;
                case SessionInfoUpdate sessionInfoUpdate:
                    entry.Title = sessionInfoUpdate.Title;
                    break;
                case ConfigUpdateUpdate configUpdate:
                    entry.ConfigOptions = configUpdate.ConfigOptions;
                    break;
                case ConfigOptionUpdate configOptionUpdate:
                    entry.ConfigOptions = configOptionUpdate.ConfigOptions;
                    break;
            }

            return entry;
        }

        private static string GetSessionUpdateType(SessionUpdate update) =>
            update switch
            {
                AgentMessageUpdate => "agent_message_chunk",
                AgentThoughtUpdate => "agent_thought_chunk",
                ToolCallUpdate => "tool_call",
                ToolCallStatusUpdate => "tool_call_update",
                PlanUpdate => "plan",
                CurrentModeUpdate => "current_mode_update",
                ConfigUpdateUpdate => "config_options_update",
                AvailableCommandsUpdate => "available_commands_update",
                ConfigOptionUpdate => "config_option_update",
                SessionInfoUpdate => "session_info_update",
                UsageUpdate => "usage_update",
                _ => "unknown"
            };

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
            var sessionMode = _sessionManager.GetSession(sessionId)?.Mode;
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

            _sessionManager.UpdateSession(
                sessionId,
                session => session.Mode = CloneModeState(projectedState) ?? new SessionModeState(),
                updateActivity: false);
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
        {
            if (source is null)
            {
                return null;
            }

            var clone = new SessionModeState
            {
                CurrentModeId = source.CurrentModeId,
                AvailableModes = source.AvailableModes
                    .Select(static mode => new DomainSessionMode(mode.Id, mode.Name, mode.Description))
                    .ToList()
            };
            clone.CurrentMode = clone.GetModeById(clone.CurrentModeId);
            return clone;
        }

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
                    null,
                    SessionState.Active,
                    null,
                    []);
            }

            return new SessionSnapshot(
                sessionId,
                true,
                configOptionsAuthoritative,
                session.Cwd,
                session.State,
                CloneModeState(session.Mode),
                session.History.ToList());
        }

        private void RestoreSessionSnapshot(SessionSnapshot snapshot)
        {
            if (!snapshot.Existed)
            {
                _sessionManager.RemoveSession(snapshot.SessionId);
                RestoreConfigAuthority(snapshot);
                return;
            }

            _sessionManager.UpdateSession(
                snapshot.SessionId,
                session =>
                {
                    session.Cwd = snapshot.Cwd;
                    session.State = snapshot.State;
                    session.Mode = CloneModeState(snapshot.Mode) ?? new SessionModeState();
                    session.History.Clear();
                    foreach (var entry in snapshot.History)
                    {
                        session.History.Add(entry);
                    }
                },
                updateActivity: false);
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
                string? cwd,
                SessionState state,
                SessionModeState? mode,
                List<SessionUpdateEntry> history)
            {
                SessionId = sessionId;
                Existed = existed;
                ConfigOptionsAuthoritative = configOptionsAuthoritative;
                Cwd = cwd;
                State = state;
                Mode = mode;
                History = history;
            }

            public string SessionId { get; }

            public bool Existed { get; }

            public bool ConfigOptionsAuthoritative { get; }

            public string? Cwd { get; }

            public SessionState State { get; }

            public SessionModeState? Mode { get; }

            public List<SessionUpdateEntry> History { get; }
        }

        public async Task<InitializeResponse> InitializeAsync(InitializeParams @params)
        {
            try
            {
                var response = await _acpClient.InitializeAsync(@params);
                return response;
            }
            catch (Exception ex)
            {
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
            try
            {
                var response = await _acpClient.CreateSessionAsync(@params);
                _currentSessionId = response.SessionId;
                _currentPlan = null;
                _currentMode = null;

                if (!string.IsNullOrWhiteSpace(response.SessionId))
                {
                    var session = await GetOrCreateSessionAsync(response.SessionId, @params.Cwd).ConfigureAwait(false);
                    session.History.Clear();
                    session.State = SessionState.Active;
                    ApplySessionResponseModeState(response.SessionId, response.Modes, response.ConfigOptions);
                }

                return response;
            }
            catch (Exception ex)
            {
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
            var previousSessionId = _currentSessionId;
            var previousPlan = ClonePlan(_currentPlan);
            var previousMode = CloneModeState(_currentMode);
            var targetSnapshot = CaptureSessionSnapshot(@params.SessionId);

            try
            {
                // CRITICAL: We update _currentSessionId *before* LoadSessionAsync 
                // because the loading process triggers Replay events, which must be 
                // associated with the new session ID immediately.
                _currentSessionId = @params.SessionId;
                _currentPlan = null;
                _currentMode = null;

                var session = await GetOrCreateSessionAsync(@params.SessionId, @params.Cwd).ConfigureAwait(false);
                session.Cwd = @params.Cwd;
                
                // Clear history before loading to ensure we don't have duplicate entries 
                // if the server replays the history during the load process.
                _sessionManager.UpdateSession(@params.SessionId, s => s.History.Clear());

                var response = await _acpClient.LoadSessionAsync(@params, cancellationToken).ConfigureAwait(false);
                try
                {
                    session.Cwd = @params.Cwd;
                    session.State = SessionState.Active;
                    ApplySessionResponseModeState(@params.SessionId, response.Modes, response.ConfigOptions);
                }
                catch
                {
                    // Ignore session tracking failures
                }
                return response;
            }
            catch (OperationCanceledException)
            {
                RestoreSessionSnapshot(targetSnapshot);

                _currentSessionId = previousSessionId;
                _currentPlan = previousPlan;
                _currentMode = previousMode;
                throw;
            }
            catch (Exception ex)
            {
                RestoreSessionSnapshot(targetSnapshot);

                _currentSessionId = previousSessionId;
                _currentPlan = previousPlan;
                _currentMode = previousMode;

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

        public Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params)
            => ResumeSessionAsync(@params, CancellationToken.None);

        public async Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params, CancellationToken cancellationToken)
        {
            var previousSessionId = _currentSessionId;
            var previousPlan = ClonePlan(_currentPlan);
            var previousMode = CloneModeState(_currentMode);
            var targetSnapshot = CaptureSessionSnapshot(@params.SessionId);

            try
            {
                _currentSessionId = @params.SessionId;
                _currentPlan = null;
                _currentMode = null;

                var session = await GetOrCreateSessionAsync(@params.SessionId, @params.Cwd).ConfigureAwait(false);
                session.Cwd = @params.Cwd;

                var response = await _acpClient.ResumeSessionAsync(@params, cancellationToken).ConfigureAwait(false);
                session.Cwd = @params.Cwd;
                session.State = SessionState.Active;
                ApplySessionResponseModeState(@params.SessionId, response.Modes, response.ConfigOptions);
                return response;
            }
            catch (OperationCanceledException)
            {
                RestoreSessionSnapshot(targetSnapshot);

                _currentSessionId = previousSessionId;
                _currentPlan = previousPlan;
                _currentMode = previousMode;
                throw;
            }
            catch (Exception ex)
            {
                RestoreSessionSnapshot(targetSnapshot);

                _currentSessionId = previousSessionId;
                _currentPlan = previousPlan;
                _currentMode = previousMode;

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

                if (string.Equals(_currentSessionId, @params.SessionId, StringComparison.Ordinal))
                {
                    _currentSessionId = null;
                    _currentPlan = null;
                    _currentMode = null;
                }
                _configAuthoritativeSessionIds.Remove(@params.SessionId);

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
            try
            {
                var response = await _acpClient.SendPromptAsync(@params, cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
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
                    ApplyCurrentModeId(@params.SessionId, @params.ModeId);
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

        public async Task<SessionCancelResponse> CancelSessionAsync(SessionCancelParams @params)
        {
            try
            {
                var response = await _acpClient.CancelSessionAsync(@params);
                _sessionManager.UpdateSession(@params.SessionId, s =>
                {
                    s.State = SessionState.Cancelled;
                });

                return response;
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
                _currentSessionId = null;
                _currentPlan = null;
                _currentMode = null;
                _configAuthoritativeSessionIds.Clear();

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

        public Task<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?> GetAvailableModesAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentSessionId))
                {
                    return Task.FromResult<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?>(null);
                }

                return Task.FromResult<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?>(
                    _currentMode is null ? null : ToProtocolModes(_currentMode));
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
            if (!string.IsNullOrWhiteSpace(_currentSessionId))
            {
                _sessionManager.UpdateSession(_currentSessionId, s => s.History.Clear());
            }
            _currentPlan = null;
            _currentMode = null;
        }

        public void Dispose()
        {
            _acpClient.SessionUpdateReceived -= OnSessionUpdateReceived;
            _acpClient.PermissionRequestReceived -= OnPermissionRequestReceived;
            _acpClient.FileSystemRequestReceived -= OnFileSystemRequestReceived;
            _acpClient.TerminalRequestReceived -= OnTerminalRequestReceived;
            _acpClient.TerminalStateChangedReceived -= OnTerminalStateChangedReceived;
            _acpClient.AskUserRequestReceived -= OnAskUserRequestReceived;
            _acpClient.ErrorOccurred -= OnErrorOccurred;
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
