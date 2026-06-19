using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;
using SalmonEgg.Infrastructure.Serialization;
using SalmonEgg.Infrastructure.Logging;
namespace SalmonEgg.Infrastructure.Client
{
    /// <summary>
    /// ACP 客户端核心实现。
    /// 整合了消息层、协议层、传输层和安全层，提供完整的 ACP 客户端功能。
    /// </summary>
    public class AcpClient : IAcpClient, IDisposable
    {
        private sealed record PendingInboundRequest(
            string Method,
            object? MessageId,
            string? SessionId = null,
            AskUserRequest? AskUserRequest = null);
        private readonly ITransport _transport;
        private readonly IMessageParser _parser;
        private readonly IMessageValidator _validator;
        private readonly ISessionManager _sessionManager;
        private readonly IPathValidator _pathValidator;
        private readonly ITerminalSessionManager _terminalSessionManager;
        private readonly IErrorLogger _errorLogger;

        
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();
        // Inbound tool requests (agent -> client) are correlated by request id so we can format responses correctly.
        private readonly ConcurrentDictionary<string, PendingInboundRequest> _pendingInboundRequests = new();

        private readonly object _lock = new();
        private bool _disposed;
        private CancellationTokenSource? _messageLoopCts;

        private bool _isInitialized;
        private AgentInfo? _agentInfo;
        private AgentCapabilities? _agentCapabilities;
        private ClientCapabilities? _clientCapabilities;
        private long _nextMessageId;
        private bool SupportsSessionList => _agentCapabilities?.SupportsSessionList == true;
        private bool SupportsSessionLoad => _agentCapabilities?.SupportsSessionLoading == true;
        private bool SupportsSessionResume => _agentCapabilities?.SupportsSessionResume == true;
        private bool SupportsSessionClose => _agentCapabilities?.SupportsSessionClose == true;

        /// <summary>
        /// 初始化事件。
        /// </summary>
        public event EventHandler<InitializeResponse>? Initialized;

        /// <summary>
        /// 会话更新事件。
        /// </summary>
        public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived;

        /// <summary>
        /// 权限请求事件。
        /// </summary>
        public event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived;

        /// <summary>
        /// 文件系统请求事件。
        /// </summary>
        public event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived;

        /// <summary>
        /// 终端请求事件。
        /// </summary>
        public event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived;

        /// <summary>
        /// 终端状态事件。
        /// </summary>
        public event EventHandler<TerminalStateChangedEventArgs>? TerminalStateChangedReceived;

        /// <summary>
        /// Ask-user 请求事件。
        /// </summary>
        public event EventHandler<AskUserRequestEventArgs>? AskUserRequestReceived;

        /// <summary>
        /// 连接错误事件。
        /// </summary>
        public event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// 判断客户端是否已初始化。
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// 判断是否已连接到 Agent。
        /// </summary>
        public bool IsConnected => _transport.IsConnected;

        /// <summary>
        /// 获取当前的 Agent 信息。
        /// </summary>
        public AgentInfo? AgentInfo => _agentInfo;

        /// <summary>
        /// 获取当前的 Agent 能力。
        /// </summary>
        public AgentCapabilities? AgentCapabilities => _agentCapabilities;

        /// <summary>
        /// 创建新的 AcpClient 实例。
        /// </summary>
        /// <param name="transport">传输层对象</param>
        /// <param name="parser">消息解析器（可选）</param>
        /// <param name="validator">消息验证器（可选）</param>
        public AcpClient(
            ITransport transport,
            IMessageParser? parser = null,
            IMessageValidator? validator = null,
            IErrorLogger? errorLogger = null,
            ISessionManager? sessionManager = null,
            ITerminalSessionManager? terminalSessionManager = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _parser = parser ?? new MessageParser();
            _validator = validator ?? new MessageValidator();
            _sessionManager = sessionManager ?? new Services.SessionManager();
            _pathValidator = new Services.Security.PathValidator();
            _terminalSessionManager = terminalSessionManager ?? new Services.UnsupportedTerminalSessionManager();
            _errorLogger = errorLogger ?? new Logging.ErrorLogger();

            // 注册传输层事件
            _transport.MessageReceived += OnMessageReceived;
            _transport.ErrorOccurred += OnTransportError;
        }

        /// <summary>
        /// 初始化与 Agent 的连接。
        /// </summary>
       public async Task<InitializeResponse> InitializeAsync(InitializeParams @params, CancellationToken cancellationToken = default)
           {
               if (_isInitialized)
               {
                   throw new InvalidOperationException("客户端已初始化");
               }

               // 确保传输层已连接
              if (!_transport.IsConnected)
              {
                  var connected = await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
                  if (!connected)
                  {
                      throw new InvalidOperationException("无法连接到传输层");
                  }
              }

               // 发送 initialize 请求
               var request = new JsonRpcRequest(
                   Interlocked.Increment(ref _nextMessageId),
                   "initialize",
                   ToElement(@params, AcpJsonContext.Default.InitializeParams));
               var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            // 验证响应
            var validationResult = _validator.ValidateResponse(response);
            if (!validationResult.IsValid)
            {
                throw new AcpException(JsonRpcErrorCode.InvalidRequest, $"响应验证失败：{string.Join("; ", validationResult.Errors)}");
            }

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            // 解析响应
            var initializeResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.InitializeResponse);
            if (initializeResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse initialize response");
            }

            // Validate protocol compatibility.
            // We allow older server versions but reject newer versions that this client cannot understand.
            var serverVersion = initializeResponse.ProtocolVersion;
            var clientVersion = @params.ProtocolVersion;

            if (serverVersion > clientVersion)
            {
                throw new AcpException(
                    JsonRpcErrorCode.ProtocolVersionMismatch,
                    $"Protocol version mismatch. Max supported by client: {clientVersion}, Server: {serverVersion}");
            }

            if (serverVersion < clientVersion)
            {
                _errorLogger.LogError(new ErrorLogEntry(
                    "PROTOCOL_VERSION_DOWNLEVEL",
                    $"Server protocol version {serverVersion} is older than client requested {clientVersion}. Proceeding in compatibility mode.",
                    ErrorSeverity.Info,
                    nameof(InitializeAsync)));
            }

            // 存储 Agent 信息
            _agentInfo = initializeResponse.AgentInfo;
            _agentCapabilities = initializeResponse.AgentCapabilities;
            _clientCapabilities = @params.ClientCapabilities;
            _isInitialized = true;

            // 启动消息接收循环
            _messageLoopCts = new CancellationTokenSource();
            _ = ProcessMessageLoopAsync(_messageLoopCts.Token);

            // 触发事件
            Initialized?.Invoke(this, initializeResponse);

            return initializeResponse;
        }

        /// <summary>
        /// 创建新的会话。
        /// </summary>
        public async Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            ValidateRequiredAbsolutePath(@params.Cwd, "cwd", "session/new");
            EnsureMcpServersSupported(@params.McpServers, "session/new");

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/new",
                ToElement(@params, AcpJsonContext.Default.SessionNewParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var sessionNewResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.SessionNewResponse);
            if (sessionNewResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/new response");
            }

            // 创建会话记录
            await _sessionManager.CreateSessionAsync(sessionNewResponse.SessionId, @params.Cwd).ConfigureAwait(false);

            return sessionNewResponse;
        }

        /// <summary>
        /// 加载已有的会话。
        /// </summary>
        public async Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            if (!SupportsSessionLoad)
            {
                _errorLogger.LogError(new ErrorLogEntry(
                    "SESSION_LOAD_UNSUPPORTED",
                    "Agent does not support session/load capability",
                    ErrorSeverity.Info,
                    nameof(LoadSessionAsync)));

                return SessionLoadResponse.Completed;
            }

            ValidateRequiredAbsolutePath(@params.Cwd, "cwd", "session/load");
            EnsureMcpServersSupported(@params.McpServers, "session/load");

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/load",
                ToElement(@params, AcpJsonContext.Default.SessionLoadParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return SessionLoadResponse.Completed;
            }

            var sessionLoadResponse = FromElement(response.Result.Value, AcpJsonContext.Default.SessionLoadResponse);

            return sessionLoadResponse ?? SessionLoadResponse.Completed;
        }

        /// <summary>
        /// 恢复已有会话但不要求 Agent 重放历史。
        /// </summary>
        public async Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            if (!SupportsSessionResume)
            {
                _errorLogger.LogError(new ErrorLogEntry(
                    "SESSION_RESUME_UNSUPPORTED",
                    "Agent does not support session/resume capability",
                    ErrorSeverity.Info,
                    nameof(ResumeSessionAsync)));

                return SessionResumeResponse.Completed;
            }

            ValidateRequiredAbsolutePath(@params.Cwd, "cwd", "session/resume");
            EnsureMcpServersSupported(@params.McpServers, "session/resume");

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/resume",
                ToElement(@params, AcpJsonContext.Default.SessionResumeParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return SessionResumeResponse.Completed;
            }

            var sessionResumeResponse = FromElement(response.Result.Value, AcpJsonContext.Default.SessionResumeResponse);

            return sessionResumeResponse ?? SessionResumeResponse.Completed;
        }

        /// <summary>
        /// 关闭已有会话并释放 Agent 侧资源。
        /// </summary>
        public async Task<SessionCloseResponse> CloseSessionAsync(SessionCloseParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (!SupportsSessionClose)
            {
                _errorLogger.LogError(new ErrorLogEntry(
                    "SESSION_CLOSE_UNSUPPORTED",
                    "Agent does not support session/close capability",
                    ErrorSeverity.Info,
                    nameof(CloseSessionAsync)));

                _sessionManager.RemoveSession(@params.SessionId);
                return SessionCloseResponse.Completed;
            }

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/close",
                ToElement(@params, AcpJsonContext.Default.SessionCloseParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                _sessionManager.RemoveSession(@params.SessionId);
                return SessionCloseResponse.Completed;
            }

            var sessionCloseResponse = FromElement(response.Result.Value, AcpJsonContext.Default.SessionCloseResponse);

            _sessionManager.RemoveSession(@params.SessionId);
            return sessionCloseResponse ?? SessionCloseResponse.Completed;
        }

        /// <summary>
        /// 列出远端 Agent 支持的会话列表
        /// </summary>
        public async Task<SessionListResponse> ListSessionsAsync(SessionListParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            ValidateOptionalAbsolutePath(@params.Cwd, "cwd", "session/list");

            if (!SupportsSessionList)
            {
                _errorLogger.LogError(new ErrorLogEntry(
                    "SESSION_LIST_UNSUPPORTED",
                    "Agent does not support session/list capability",
                    ErrorSeverity.Info,
                    nameof(ListSessionsAsync)));

                return new SessionListResponse();
            }

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/list",
                ToElement(@params, AcpJsonContext.Default.SessionListParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var listResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.SessionListResponse);
            if (listResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/list response");
            }

            ValidateSessionListResponse(listResponse);
            return listResponse;
        }

        /// <summary>
        /// 向会话发送提示。
        /// </summary>
        public async Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            // 检查会话是否存在
            var session = _sessionManager.GetSession(@params.SessionId);
            if (session == null)
            {
                throw new AcpException(JsonRpcErrorCode.SessionNotFound, $"Session '{@params.SessionId}' not found");
            }

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/prompt",
                ToElement(@params, AcpJsonContext.Default.SessionPromptParams));

            // ACP requires a real session/prompt response with a protocol stopReason.
            // The client must wait for the protocol response instead of fabricating a terminal result.
            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var promptResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.SessionPromptResponse);
            if (promptResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/prompt response");
            }

            return promptResponse;
        }

        /// <summary>
        /// 设置会话模式。
        /// </summary>
        public async Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/set_mode",
                ToElement(@params, AcpJsonContext.Default.SessionSetModeParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var setModeResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.SessionSetModeResponse);
            if (setModeResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/set_mode response");
            }

            // 更新会话模式
            _sessionManager.UpdateSession(@params.SessionId, session =>
            {
                session.Mode.CurrentModeId = setModeResponse.ModeId;
            });

            return setModeResponse;
        }

        /// <summary>
        /// 设置会话配置选项。
        /// </summary>
        public async Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/set_config_option",
                ToElement(@params, AcpJsonContext.Default.SessionSetConfigOptionParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var configResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.SessionSetConfigOptionResponse);
            if (configResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/set_config_option response");
            }

            return configResponse;
        }

        /// <summary>
        /// 取消会话。
        /// </summary>
        public async Task<SessionCancelResponse> CancelSessionAsync(SessionCancelParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            // ACP defines session/cancel as a notification (no response expected).
            var notification = new JsonRpcNotification(
                "session/cancel",
                ToElement(@params, AcpJsonContext.Default.SessionCancelParams));

            await _transport.SendMessageAsync(
                _parser.SerializeMessage(notification),
                cancellationToken).ConfigureAwait(false);

            await CancelPendingInboundRequestsForSessionAsync(@params.SessionId).ConfigureAwait(false);

            // 更新会话状态
            await _sessionManager.CancelSessionAsync(@params.SessionId, @params.Reason).ConfigureAwait(false);

            return new SessionCancelResponse(success: true);
        }

        /// <summary>
        /// 执行认证。
        /// </summary>
        public async Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "authenticate",
                ToElement(@params, AcpJsonContext.Default.AuthenticateParams));

            var response = await SendRequestAsync(request, cancellationToken);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var authResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.AuthenticateResponse);
            if (authResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse authenticate response");
            }

            return authResponse;
        }

        /// <summary>
        /// 响应权限请求。
        /// </summary>
        public async Task<bool> RespondToPermissionRequestAsync(object? messageId, string outcome, string? optionId = null)
        {
            if (messageId == null)
            {
                return false;
            }

            return await TrySendPermissionOutcomeResponseAsync(messageId, outcome, optionId).ConfigureAwait(false);
        }

        /// <summary>
        /// 响应文件系统请求。
        /// </summary>
        public async Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
        {
            return await TrySendFileSystemResponseAsync(messageId, success, content, message).ConfigureAwait(false);
        }

        /// <summary>
        /// 响应 ask-user 请求。
        /// </summary>
        public async Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers)
        {
            if (answers == null)
            {
                throw new ArgumentNullException(nameof(answers));
            }

            return await TrySendAskUserResponseAsync(messageId, answers).ConfigureAwait(false);
        }

        private async Task<bool> TrySendPermissionOutcomeResponseAsync(object? messageId, string outcome, string? optionId)
        {
            if (messageId == null)
            {
                return false;
            }

            // Only respond once per inbound request id. Unknown or stale ids are not a
            // protocol payload, so they should not fail ACP schema validation.
            var idStr = messageId.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(idStr)
                || !TryTakePendingInboundRequest(idStr, out _))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(outcome))
            {
                return false;
            }

            if (string.Equals(outcome, "selected", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(optionId))
                {
                    throw new AcpException(JsonRpcErrorCode.InvalidParams, "Permission outcome 'selected' requires optionId.");
                }
            }
            else if (!string.Equals(outcome, "cancelled", StringComparison.Ordinal))
            {
                throw new AcpException(JsonRpcErrorCode.InvalidParams, $"Unsupported permission outcome '{outcome}'.");
            }

            var outcomePayload = new PermissionOutcomeResult
            {
                Outcome = new PermissionOutcome
                {
                    Outcome = outcome,
                    OptionId = string.IsNullOrWhiteSpace(optionId) ? null : optionId
                }
            };

            var response = new JsonRpcResponse(
                messageId,
                ToElement(outcomePayload, AcpJsonContext.Default.PermissionOutcomeResult));
            return await SendResponseAsync(response).ConfigureAwait(false);
        }

        private async Task<bool> TrySendFileSystemResponseAsync(object messageId, bool success, string? content, string? message)
        {
            var idStr = messageId?.ToString() ?? string.Empty;
            if (!TryTakePendingInboundRequest(idStr, out var pending))
            {
                return false;
            }

            if (!success)
            {
                // Use a JSON-RPC error instead of a success=false payload (ACP tools follow JSON-RPC semantics).
                var error = new JsonRpcError(
                    JsonRpcErrorCode.PermissionDenied,
                    string.IsNullOrWhiteSpace(message) ? "Permission denied" : message);
                return await SendResponseAsync(new JsonRpcResponse(messageId, error)).ConfigureAwait(false);
            }

            JsonElement result;
            if (string.Equals(pending.Method, "fs/read_text_file", StringComparison.Ordinal))
            {
                result = ToElement(
                    new ReadTextFileResult { Content = content ?? string.Empty },
                    AcpJsonContext.Default.ReadTextFileResult);
            }
            else
            {
                // fs/write_text_file returns null on success.
                result = NullJsonElement();
            }

            return await SendResponseAsync(new JsonRpcResponse(messageId, result)).ConfigureAwait(false);
        }

        private async Task<bool> TrySendAskUserResponseAsync(object messageId, IReadOnlyDictionary<string, string> answers)
        {
            var idStr = messageId?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return false;
            }

            if (!TryTakePendingInboundRequest(idStr, out var pending))
            {
                return false;
            }

            if (pending.AskUserRequest == null)
            {
                return false;
            }

            AskUserContract.ValidateAnswers(pending.AskUserRequest, answers);
            var response = new AskUserResponse(pending.AskUserRequest.Questions, answers);
            return await SendResponseAsync(
                new JsonRpcResponse(
                    messageId,
                    ToElement(response, AcpJsonContext.Default.AskUserResponse))).ConfigureAwait(false);
        }

        /// <summary>
        /// 断开与 Agent 的连接。
        /// </summary>
        public async Task<bool> DisconnectAsync()
        {
            _messageLoopCts?.Cancel();
            CancelPendingRequests();

            await _transport.DisconnectAsync();
            return true;
        }

        /// <summary>
        /// 发送请求并等待响应。
        /// </summary>
        
        private async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            var requestIdStr = request.Id?.ToString() ?? string.Empty;
            var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[requestIdStr] = tcs;

            try
            {
                var json = _parser.SerializeMessage(request);
                await _transport.SendMessageAsync(json, cancellationToken).ConfigureAwait(false);
                using var cancellationRegistration = cancellationToken.Register(
                    static state => ((TaskCompletionSource<JsonRpcResponse>)state!).TrySetCanceled(),
                    tcs);
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (TaskCanceledException ex)
            {
                throw new OperationCanceledException(
                    "ACP request was canceled because the transport disconnected.",
                    ex);
            }
            catch (Exception ex)
            {
                _errorLogger.LogError(new ErrorLogEntry("REQ_ERROR", $"[AcpClient.SendRequestAsync] Request {requestIdStr} failed: {ex.Message}", ErrorSeverity.Error, "SendRequestAsync", null, ex));
                throw;
            }
            finally
            {
                _pendingRequests.TryRemove(requestIdStr, out _);
            }
        }

        private void CancelPendingRequests()
        {
            foreach (var pendingRequest in _pendingRequests)
            {
                if (_pendingRequests.TryRemove(pendingRequest.Key, out var pending))
                {
                    pending.TrySetCanceled();
                }
            }
        }

        /// <summary>
        /// 发送响应（用于通知请求的响应）。
        /// </summary>
        private async Task<bool> SendResponseAsync(JsonRpcResponse response)
        {
            try
            {
                var json = _parser.SerializeMessage(response);
                await _transport.SendMessageAsync(json).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to send response: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 处理消息接收事件。
        /// </summary>
        private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            try
            {
                var message = _parser.ParseMessage(e.Message);

                
                if (message is JsonRpcResponse response)
                {
                    var responseIdStr = response.Id?.ToString() ?? string.Empty;
                    // 匹配 pending 请求
                    if (_pendingRequests.TryRemove(responseIdStr, out var tcs))
                    {
                        tcs.TrySetResult(response);
                    }
                }

                else if (message is JsonRpcRequest request)
                {
                    // Agent -> client tool invocation (requires a JSON-RPC response).
                    HandleRequest(request);
                }
                else if (message is JsonRpcNotification notification)
                {
                    // 处理通知
                    HandleNotification(notification);
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process message: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理通知消息。
        /// </summary>
        private void HandleNotification(JsonRpcNotification notification)
        {
            switch (notification.Method)
            {
                case "session/update":
                    HandleSessionUpdate(notification);
                    break;
                default:
                    // 未知通知类型
                    break;
            }
        }

        /// <summary>
        /// 处理请求消息（Agent -> Client，需要返回响应）。
        /// </summary>
        private void HandleRequest(JsonRpcRequest request)
        {
            var requestIdStr = request.Id?.ToString() ?? string.Empty;

            switch (request.Method)
            {
                case "session/request_permission":
                    if (!string.IsNullOrWhiteSpace(requestIdStr))
                    {
                        TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    }
                    HandlePermissionRequest(request);
                    break;
                case "fs/read_text_file":
                case "fs/write_text_file":
                    if (!SupportsAdvertisedFileSystemCapability(request.Method))
                    {
                        RejectUnsupportedClientRequest(request);
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(requestIdStr))
                    {
                        TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    }
                    HandleFileSystemRequest(request);
                    break;
                case "terminal/create":
                case "terminal/output":
                case "terminal/wait_for_exit":
                case "terminal/kill":
                case "terminal/release":
                    if (_clientCapabilities?.Terminal != true)
                    {
                        RejectUnsupportedClientRequest(request);
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(requestIdStr))
                    {
                        TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    }
                    _ = HandleTerminalRequestAsync(request);
                    break;
                case ClientCapabilityMetadata.AskUserExtensionMethod:
                    if (!SupportsAdvertisedAskUserExtension(request.Method))
                    {
                        RejectUnsupportedClientRequest(request);
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(requestIdStr))
                    {
                        TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    }
                    HandleAskUserRequest(request);
                    break;
                default:
                    // Best-effort: respond with "method not found" so the agent doesn't hang waiting.
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(
                        request.Id,
                        JsonRpcError.CreateMethodNotFound(request.Method)));
                    break;
            }
        }

        private bool SupportsAdvertisedFileSystemCapability(string method)
            => method switch
            {
                "fs/read_text_file" => _clientCapabilities?.Fs?.ReadTextFile == true,
                "fs/write_text_file" => _clientCapabilities?.Fs?.WriteTextFile == true,
                _ => false
            };

        private bool SupportsAdvertisedAskUserExtension(string method)
            => _clientCapabilities?.SupportsExtension(method) == true;

        private void EnsureMcpServersSupported(IEnumerable<McpServer>? mcpServers, string method)
        {
            if (mcpServers == null)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidParams,
                    $"{method} requires mcpServers to be an array.");
            }

            var result = McpServerSupportPolicy.Validate(mcpServers, _agentCapabilities);
            if (result.IsSupported)
            {
                return;
            }

            throw new AcpException(
                JsonRpcErrorCode.InvalidParams,
                $"{method} contains unsupported MCP server configuration: {result.ErrorMessage}");
        }

        private void RejectUnsupportedClientRequest(JsonRpcRequest request)
        {
            RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
            _ = SendResponseAsync(new JsonRpcResponse(
                request.Id,
                JsonRpcError.CreateMethodNotFound(request.Method)));
        }

        /// <summary>
        /// 处理会话更新通知。
        /// </summary>
        private void HandleSessionUpdate(JsonRpcNotification notification)
        {
            try
            {
                if (!notification.Params.HasValue)
                {
                    return;
                }

                var updateParams = FromElement(notification.Params.Value, AcpJsonContext.Default.SessionUpdateParams);
                if (updateParams == null || updateParams.Update == null)
                {
                    return;
                }
                SessionUpdateReceived?.Invoke(this, new SessionUpdateEventArgs(updateParams.SessionId, updateParams.Update));
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process session/update notification: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理权限请求通知。
        /// </summary>
        private void HandlePermissionRequest(JsonRpcRequest request)
        {
            try
            {
                if (!request.Params.HasValue)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp)
                    || sessionIdProp.ValueKind != JsonValueKind.String)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
                if (request.Id == null)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                var messageId = request.Id!;
                var requestId = messageId.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(requestId))
                {
                    SetPendingInboundSessionId(requestId, sessionId);
                }
                if (!rawParams.TryGetProperty("toolCall", out var toolCall)
                    || toolCall.ValueKind != JsonValueKind.Object)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing toolCall")));
                    return;
                }

                if (!toolCall.TryGetProperty("toolCallId", out var toolCallId)
                    || toolCallId.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(toolCallId.GetString()))
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing toolCallId")));
                    return;
                }

                if (!rawParams.TryGetProperty("options", out var optionsProp)
                    || optionsProp.ValueKind != JsonValueKind.Array)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing options")));
                    return;
                }

                var optionsList = new List<SalmonEgg.Domain.Services.Security.PermissionOption>();
                foreach (var option in optionsProp.EnumerateArray())
                {
                    if (option.ValueKind != JsonValueKind.Object
                        || !option.TryGetProperty("optionId", out var id)
                        || id.ValueKind != JsonValueKind.String
                        || !option.TryGetProperty("name", out var n)
                        || n.ValueKind != JsonValueKind.String
                        || !option.TryGetProperty("kind", out var k)
                        || k.ValueKind != JsonValueKind.String)
                    {
                        RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                        _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Invalid permission option")));
                        return;
                    }

                    var description = option.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                        ? d.GetString()
                        : null;
                    optionsList.Add(new SalmonEgg.Domain.Services.Security.PermissionOption(
                        id.GetString() ?? string.Empty,
                        n.GetString() ?? string.Empty,
                        k.GetString() ?? string.Empty,
                        description));
                }

                var permissionResponseFunc = new Func<string, string?, Task>((outcome, optionId) =>
                    RespondToPermissionRequestAsync(messageId, outcome, optionId));

                var eventArgs = new PermissionRequestEventArgs(
                    messageId,
                    sessionId,
                    toolCall,
                    optionsList,
                    permissionResponseFunc);

                if (PermissionRequestReceived == null)
                {
                    // No UI hooked up; cancel to avoid deadlock.
                    _ = RespondToPermissionRequestAsync(messageId, "cancelled", null);
                    return;
                }

                PermissionRequestReceived.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process permission request: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理文件系统请求通知。
        /// </summary>
        private void HandleFileSystemRequest(JsonRpcRequest request)
        {
            try
            {
                if (!request.Params.HasValue)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp) ||
                    !rawParams.TryGetProperty("path", out var pathProp))
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId or path")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
                if (request.Id == null)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                var messageId = request.Id!;
                var requestId = messageId.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(requestId))
                {
                    SetPendingInboundSessionId(requestId, sessionId);
                }
                var path = pathProp.GetString() ?? string.Empty;
                var content = rawParams.TryGetProperty("content", out var cont) ? cont.GetString() : null;

                // For legacy UI/viewmodels we expose an "operation" hint.
                var operation = request.Method switch
                {
                    "fs/read_text_file" => "read",
                    "fs/write_text_file" => "write",
                    _ => request.Method
                };
                var encoding = (string?)null;

                var fileSystemResponseFunc = new Func<bool, string?, string?, Task>((success, respContent, respMessage) =>
                RespondToFileSystemRequestAsync(messageId, success, respContent, respMessage));

                var eventArgs = new FileSystemRequestEventArgs(
                messageId,
                sessionId,
                operation,
                path,
                encoding,
                content,
                fileSystemResponseFunc);

                if (FileSystemRequestReceived == null)
                {
                    // No UI hooked up; deny to avoid deadlock.
                    _ = RespondToFileSystemRequestAsync(messageId, success: false, content: null, message: "File system requests are not supported.");
                    return;
                }

                FileSystemRequestReceived.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process file system request: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理 ask-user 请求。
        /// </summary>
        private void HandleAskUserRequest(JsonRpcRequest request)
        {
            try
            {
                if (!request.Params.HasValue)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var askUserRequest = FromElement(request.Params.Value, AcpJsonContext.Default.AskUserRequest);
                if (askUserRequest == null)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Failed to deserialize ask_user request.")));
                    return;
                }

                AskUserContract.ValidateRequest(askUserRequest);

                if (request.Id == null)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                var messageId = request.Id!;
                var requestId = messageId.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(requestId))
                {
                    SetPendingInboundAskUserRequest(requestId, askUserRequest);
                }

                if (AskUserRequestReceived == null)
                {
                    RemovePendingInboundTracking(requestId);
                    _ = SendResponseAsync(new JsonRpcResponse(
                        messageId,
                        new JsonRpcError(
                            JsonRpcErrorCode.CapabilityNotSupported,
                            "Ask-user requests are not supported.")));
                    return;
                }

                var eventArgs = new AskUserRequestEventArgs(
                    messageId,
                    askUserRequest,
                    answers => RespondToAskUserRequestAsync(messageId, answers));

                AskUserRequestReceived.Invoke(this, eventArgs);
            }
            catch (InvalidOperationException ex)
            {
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams(ex.Message)));
            }
            catch (Exception ex)
            {
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                OnErrorOccurred($"Failed to process ask_user request: {ex.Message}");
                _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInternalError(ex.Message)));
            }
        }

        /// <summary>
        /// 处理终端请求。
        /// </summary>
        private async Task HandleTerminalRequestAsync(JsonRpcRequest request)
        {
            try
            {
                if (!request.Params.HasValue)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp))
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId")));
                    return;
                }

                if (request.Id == null)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                var messageId = request.Id;
                var requestId = request.Id?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(requestId))
                {
                    SetPendingInboundSessionId(requestId, sessionId);
                }

                string? terminalId = null;
                if (rawParams.TryGetProperty("terminalId", out var terminalIdProp))
                {
                    terminalId = terminalIdProp.GetString();
                }

                TerminalRequestReceived?.Invoke(
                    this,
                    new TerminalRequestEventArgs(
                        messageId,
                        sessionId,
                        terminalId,
                        request.Method,
                        rawParams,
                        _ => Task.FromResult(false)));

                switch (request.Method)
                {
                    case "terminal/create":
                        var createRequest = FromElement(rawParams, AcpJsonContext.Default.TerminalCreateRequest)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/create request.");
                        var createResponse = await _terminalSessionManager.CreateAsync(createRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(sessionId, createResponse.TerminalId, request.Method);
                        await SendTerminalSuccessResponseAsync(messageId, createResponse).ConfigureAwait(false);
                        break;

                    case "terminal/output":
                        var outputRequest = FromElement(rawParams, AcpJsonContext.Default.TerminalOutputRequest)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/output request.");
                        var outputResponse = await _terminalSessionManager.GetOutputAsync(outputRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(
                            sessionId,
                            outputRequest.TerminalId,
                            request.Method,
                            outputResponse.Output,
                            outputResponse.Truncated,
                            outputResponse.ExitStatus);
                        await SendTerminalSuccessResponseAsync(messageId, outputResponse).ConfigureAwait(false);
                        break;

                    case "terminal/wait_for_exit":
                        var waitRequest = FromElement(rawParams, AcpJsonContext.Default.TerminalWaitForExitRequest)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/wait_for_exit request.");
                        var waitResponse = await _terminalSessionManager.WaitForExitAsync(waitRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(
                            sessionId,
                            waitRequest.TerminalId,
                            request.Method,
                            exitStatus: new TerminalExitStatus
                            {
                                ExitCode = waitResponse.ExitCode,
                                Signal = waitResponse.Signal
                            });
                        await SendTerminalSuccessResponseAsync(messageId, waitResponse).ConfigureAwait(false);
                        break;

                    case "terminal/kill":
                        var killRequest = FromElement(rawParams, AcpJsonContext.Default.TerminalKillRequest)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/kill request.");
                        var killResponse = await _terminalSessionManager.KillAsync(killRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(sessionId, killRequest.TerminalId, request.Method);
                        await SendTerminalSuccessResponseAsync(messageId, killResponse).ConfigureAwait(false);
                        break;

                    case "terminal/release":
                        var releaseRequest = FromElement(rawParams, AcpJsonContext.Default.TerminalReleaseRequest)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/release request.");
                        var releaseResponse = await _terminalSessionManager.ReleaseAsync(releaseRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(
                            sessionId,
                            releaseRequest.TerminalId,
                            request.Method,
                            isReleased: true);
                        await SendTerminalSuccessResponseAsync(messageId, releaseResponse).ConfigureAwait(false);
                        break;

                    default:
                        RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                        await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateMethodNotFound(request.Method))).ConfigureAwait(false);
                        break;
                }
            }
            catch (KeyNotFoundException ex)
            {
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams(ex.Message))).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams(ex.Message))).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                await SendResponseAsync(new JsonRpcResponse(
                    request.Id,
                    new JsonRpcError(JsonRpcErrorCode.CapabilityNotSupported, ex.Message))).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process terminal request: {ex.Message}");
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInternalError(ex.Message))).ConfigureAwait(false);
            }
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalCreateResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement(result, AcpJsonContext.Default.TerminalCreateResponse)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalOutputResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement(result, AcpJsonContext.Default.TerminalOutputResponse)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalWaitForExitResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement(result, AcpJsonContext.Default.TerminalWaitForExitResponse)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalKillResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement(result, AcpJsonContext.Default.TerminalKillResponse)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalReleaseResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement(result, AcpJsonContext.Default.TerminalReleaseResponse)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, JsonElement result)
        {
            RemovePendingInboundTracking(messageId?.ToString() ?? string.Empty);
            await SendResponseAsync(new JsonRpcResponse(messageId, result)).ConfigureAwait(false);
        }

        private void PublishTerminalStateChanged(
            string sessionId,
            string terminalId,
            string method,
            string? output = null,
            bool? truncated = null,
            TerminalExitStatus? exitStatus = null,
            bool isReleased = false)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(terminalId))
            {
                return;
            }

            TerminalStateChangedReceived?.Invoke(
                this,
                new TerminalStateChangedEventArgs(
                    sessionId,
                    terminalId,
                    method,
                    output,
                    truncated,
                    exitStatus,
                    isReleased));
        }

        private async Task CancelPendingInboundRequestsForSessionAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var pendingIds = _pendingInboundRequests
                .Where(pair => string.Equals(pair.Value.SessionId, sessionId, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var pendingId in pendingIds)
            {
                if (!TryGetPendingInboundRequest(pendingId, out var pending))
                {
                    RemovePendingInboundTracking(pendingId);
                    continue;
                }

                if (pending.MessageId == null)
                {
                    RemovePendingInboundTracking(pendingId);
                    continue;
                }

                if (string.Equals(pending.Method, "session/request_permission", StringComparison.Ordinal))
                {
                    await TrySendPermissionOutcomeResponseAsync(pending.MessageId, "cancelled", null).ConfigureAwait(false);
                    continue;
                }

                RemovePendingInboundTracking(pendingId);
                await SendResponseAsync(new JsonRpcResponse(
                    pending.MessageId,
                    new JsonRpcError(
                        JsonRpcErrorCode.MethodNotAllowed,
                        "Session was cancelled before the client completed this request."))).ConfigureAwait(false);
            }
        }

        private void RemovePendingInboundTracking(string idStr)
        {
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return;
            }

            _pendingInboundRequests.TryRemove(idStr, out _);
        }

        private void TrackPendingInboundRequest(string idStr, string method, object? messageId)
        {
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return;
            }

            _pendingInboundRequests[idStr] = new PendingInboundRequest(method, messageId);
        }

        private bool TryGetPendingInboundRequest(string idStr, out PendingInboundRequest pending)
        {
            pending = default!;
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return false;
            }

            return _pendingInboundRequests.TryGetValue(idStr, out pending!);
        }

        private bool TryTakePendingInboundRequest(string idStr, out PendingInboundRequest pending)
        {
            pending = default!;
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return false;
            }

            return _pendingInboundRequests.TryRemove(idStr, out pending!);
        }

        private void SetPendingInboundSessionId(string idStr, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return;
            }

            while (_pendingInboundRequests.TryGetValue(idStr, out var existing))
            {
                var updated = existing with { SessionId = sessionId };
                if (_pendingInboundRequests.TryUpdate(idStr, updated, existing))
                {
                    return;
                }
            }
        }

        private void SetPendingInboundAskUserRequest(string idStr, AskUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return;
            }

            _pendingInboundRequests.AddOrUpdate(
                idStr,
                _ => new PendingInboundRequest(
                    Method: ClientCapabilityMetadata.AskUserExtensionMethod,
                    MessageId: null,
                    SessionId: request.SessionId,
                    AskUserRequest: request),
                (_, existing) => existing with
                {
                    Method = string.IsNullOrWhiteSpace(existing.Method) ? ClientCapabilityMetadata.AskUserExtensionMethod : existing.Method,
                    SessionId = request.SessionId,
                    AskUserRequest = request
                });
        }

        /// <summary>
        /// 处理传输错误事件。
        /// </summary>
        private void OnTransportError(object? sender, TransportErrorEventArgs e)
        {
            OnErrorOccurred(EnrichTransportErrorMessage(e.ErrorMessage));
        }

        /// <summary>
        /// 触发错误事件。
        /// </summary>
        private void OnErrorOccurred(string errorMessage)
        {
            _errorLogger.LogError(new ErrorLogEntry("CLIENT_ERROR", errorMessage, ErrorSeverity.Error));
            ErrorOccurred?.Invoke(this, errorMessage);
        }

        private static string EnrichTransportErrorMessage(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return errorMessage;
            }

            if (!errorMessage.Contains("进程", StringComparison.Ordinal) &&
                !errorMessage.Contains("stdout", StringComparison.OrdinalIgnoreCase) &&
                !errorMessage.Contains("stderr", StringComparison.OrdinalIgnoreCase))
            {
                return errorMessage;
            }

            const string sshBridgeGuidance =
                " 如果这是 SSH stdio bridge，请避免使用 ssh -t，确保 stdout 只输出 ACP 帧，并优先启用 BatchMode=yes。";

            return errorMessage.Contains("ssh -t", StringComparison.Ordinal)
                ? errorMessage
                : errorMessage + sshBridgeGuidance;
        }

        /// <summary>
        /// 处理消息循环。
        /// </summary>
        private async Task ProcessMessageLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _transport.IsConnected)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        /// <summary>
        /// 确保客户端已初始化。
        /// </summary>
        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("ACP client is not initialized. Call InitializeAsync first.");
            }
        }

        private void ValidateRequiredAbsolutePath(string? path, string fieldName, string methodName)
        {
            if (string.IsNullOrWhiteSpace(path) || !_pathValidator.IsAbsolutePath(path))
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidParams,
                    $"{methodName} requires '{fieldName}' to be an absolute path.");
            }
        }

        private void ValidateOptionalAbsolutePath(string? path, string fieldName, string methodName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            ValidateRequiredAbsolutePath(path, fieldName, methodName);
        }

        private void ValidateSessionListResponse(SessionListResponse response)
        {
            foreach (var session in response.Sessions)
            {
                if (string.IsNullOrWhiteSpace(session.SessionId))
                {
                    throw new AcpException(
                        JsonRpcErrorCode.ParseError,
                        "Invalid session/list response: sessionId is required.");
                }

                if (string.IsNullOrWhiteSpace(session.Cwd) || !_pathValidator.IsAbsolutePath(session.Cwd))
                {
                    throw new AcpException(
                        JsonRpcErrorCode.ParseError,
                        $"Invalid session/list response: session '{session.SessionId}' must include an absolute cwd.");
                }
            }
        }

        private static JsonElement ToElement<T>(T value, JsonTypeInfo<T> typeInfo) =>
            JsonSerializer.SerializeToElement(value, typeInfo);

        private static T? FromElement<T>(JsonElement value, JsonTypeInfo<T> typeInfo) =>
            value.Deserialize(typeInfo);

        private static JsonElement NullJsonElement()
        {
            using var document = JsonDocument.Parse("null");
            return document.RootElement.Clone();
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _messageLoopCts?.Cancel();
            _terminalSessionManager.Dispose();
            _ = _transport.DisconnectAsync();
            _transport.MessageReceived -= OnMessageReceived;
            _transport.ErrorOccurred -= OnTransportError;
            GC.SuppressFinalize(this);
        }
    }
}
