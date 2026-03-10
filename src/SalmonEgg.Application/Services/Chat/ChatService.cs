using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;

namespace SalmonEgg.Application.Services.Chat
{
    /// <summary>
    /// Chat 服务实现类
    /// 封装了 ACP 客户端的核心功能，提供聊天相关的服务
    /// </summary>
    public class ChatService : IChatService
    {
        private readonly IAcpClient _acpClient;
        private readonly IErrorLogger _errorLogger;
        private readonly ISessionManager _sessionManager;
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
        public event EventHandler<string>? ErrorOccurred;

        public ChatService(IAcpClient acpClient, IErrorLogger errorLogger, ISessionManager sessionManager)
        {
            _acpClient = acpClient ?? throw new ArgumentNullException(nameof(acpClient));
            _errorLogger = errorLogger ?? throw new ArgumentNullException(nameof(errorLogger));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));

            // 订阅 ACP 客户端事件
            _acpClient.SessionUpdateReceived += OnSessionUpdateReceived;
            _acpClient.PermissionRequestReceived += OnPermissionRequestReceived;
            _acpClient.FileSystemRequestReceived += OnFileSystemRequestReceived;
            _acpClient.ErrorOccurred += OnErrorOccurred;
        }

        private Session? GetSession(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            return _sessionManager.GetSession(sessionId);
        }

        private Session GetOrCreateSession(string sessionId, string? cwd = null)
        {
            var existing = _sessionManager.GetSession(sessionId);
            if (existing != null)
            {
                return existing;
            }

            // The session manager API is async; events are sync, so we do a best-effort sync creation.
            return _sessionManager.CreateSessionAsync(sessionId, cwd).GetAwaiter().GetResult();
        }

        private void OnSessionUpdateReceived(object? sender, SessionUpdateEventArgs e)
        {
            if (e.Update != null)
            {
                // 更新会话历史
                var entry = CreateSessionUpdateEntry(e.Update, e.SessionId);
                if (entry != null)
                {
                    try
                    {
                        GetOrCreateSession(e.SessionId).AddHistoryEntry(entry);
                    }
                    catch
                    {
                        // Ignore session tracking failures; UI will still receive SessionUpdateReceived.
                    }

                    // 处理不同类型的更新
                    switch (e.Update)
                    {
                        case AgentMessageUpdate messageUpdate:
                            // 处理消息更新
                            break;
                        case AgentThoughtUpdate thoughtUpdate:
                            // 思考片段：可选择忽略或用于调试
                            break;
                        case ToolCallUpdate toolCallUpdate:
                            // 处理工具调用更新
                            break;
                        case ToolCallStatusUpdate toolCallStatusUpdate:
                            // 工具调用状态更新（某些 Agent 使用 tool_call_update）
                            break;
                        case PlanUpdate planUpdate:
                            // 更新当前计划
                            if (planUpdate.Entries != null)
                            {
                                _currentPlan = new Plan { Entries = planUpdate.Entries };
                            }
                            break;
                        case ModeChangeUpdate modeChange:
                            // 更新当前模式
                            if (!string.IsNullOrEmpty(modeChange.ModeId))
                            {
                                _currentMode = new SessionModeState { CurrentModeId = modeChange.ModeId };
                            }
                            break;
                        case ConfigOptionUpdate configOption:
                            // 配置选项更新（当前实现仅记录到 history）
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
                    entry.ToolCall = toolCallUpdate.ToolCall;
                    entry.Kind = toolCallUpdate.Kind;
                    entry.Status = toolCallUpdate.Status;
                    entry.Title = toolCallUpdate.Title;
                    break;
                case ToolCallStatusUpdate toolCallStatusUpdate:
                    entry.ToolCallId = toolCallStatusUpdate.ToolCallId;
                    entry.Status = toolCallStatusUpdate.Status;
                    break;
                case PlanUpdate planUpdate:
                    entry.Entries = planUpdate.Entries;
                    entry.Title = planUpdate.Title;
                    break;
                case ModeChangeUpdate modeChange:
                    entry.ModeId = modeChange.ModeId;
                    entry.Title = modeChange.Title;
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
                ModeChangeUpdate => "current_mode_update",
                ConfigUpdateUpdate => "config_options_update",
                ConfigOptionUpdate => "config_option_update",
                _ => "unknown"
            };

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
                    var session = GetOrCreateSession(response.SessionId, @params.Cwd);
                    session.History.Clear();
                    session.State = SessionState.Active;
                }

                // 保存会话模式信息
                if (response.Modes?.AvailableModes != null && response.Modes.AvailableModes.Count > 0)
                {
                    // 可以选择默认模式
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

        public async Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params)
        {
            var previousSessionId = _currentSessionId;
            List<SessionUpdateEntry>? previousHistory = null;
            var hadPreviousHistory = false;

            try
            {
                // Make the target session current before we start receiving replay updates.
                _currentSessionId = @params.SessionId;
                _currentPlan = null;
                _currentMode = null;

                // Avoid duplicating cached history when loading triggers a replay.
                var existing = _sessionManager.GetSession(@params.SessionId);
                if (existing != null && existing.History.Count > 0)
                {
                    hadPreviousHistory = true;
                    previousHistory = existing.History.ToList();
                }
                _sessionManager.UpdateSession(@params.SessionId, s => s.History.Clear());

                var response = await _acpClient.LoadSessionAsync(@params);
                try
                {
                    var session = GetOrCreateSession(@params.SessionId, @params.Cwd);
                    session.Cwd = @params.Cwd;
                    session.State = SessionState.Active;
                }
                catch
                {
                }
                return response;
            }
            catch (Exception ex)
            {
                if (hadPreviousHistory && previousHistory != null)
                {
                    _sessionManager.UpdateSession(@params.SessionId, s =>
                    {
                        s.History.Clear();
                        foreach (var entry in previousHistory)
                        {
                            s.History.Add(entry);
                        }
                    });
                }

                _currentSessionId = previousSessionId;

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

        public async Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params)
        {
            try
            {
                var response = await _acpClient.SendPromptAsync(@params);
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
                    _currentMode = new SessionModeState { CurrentModeId = @params.ModeId };
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

        public async Task<bool> DisconnectAsync()
        {
            try
            {
                _currentSessionId = null;
                _currentPlan = null;
                _currentMode = null;

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

        public async Task<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?> GetAvailableModesAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentSessionId))
                {
                    return null;
                }

                // 可以通过会话更新事件获取模式信息
                // 这里暂时返回 null，实际实现需要根据响应获取
                return null;
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
            _acpClient.ErrorOccurred -= OnErrorOccurred;
        }
    }
}
