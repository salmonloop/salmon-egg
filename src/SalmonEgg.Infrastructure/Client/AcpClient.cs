using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.JsonRpc;
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
        private readonly ITransport _transport;
        private readonly IMessageParser _parser;
        private readonly IMessageValidator _validator;
        private readonly ICapabilityManager _capabilityManager;
        private readonly ISessionManager _sessionManager;
        private readonly IPathValidator _pathValidator;
        private readonly IPermissionManager _permissionManager;
        private readonly IErrorLogger _errorLogger;

        
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();
        // Inbound tool requests (agent -> client) are correlated by request id so we can format responses correctly.
        private readonly ConcurrentDictionary<string, string> _pendingInboundRequestMethods = new();

        private readonly object _lock = new();
        private bool _disposed;
        private CancellationTokenSource? _messageLoopCts;

        private bool _isInitialized;
        private AgentInfo? _agentInfo;
        private AgentCapabilities? _agentCapabilities;
        private long _nextMessageId;
        private long _lastReceivedUnixMs;

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
            IErrorLogger? errorLogger = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _parser = parser ?? new MessageParser();
            _validator = validator ?? new MessageValidator();
            _capabilityManager = new Services.CapabilityManager();
            _sessionManager = new Services.SessionManager();
            _pathValidator = new Services.Security.PathValidator();
            _permissionManager = new Services.Security.PermissionManager();
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
               _errorLogger.LogError(new ErrorLogEntry("DEBUG", "[AcpClient] 开始创建 initialize 请求", ErrorSeverity.Info, nameof(InitializeAsync)));
               var request = new JsonRpcRequest(
                   Interlocked.Increment(ref _nextMessageId),
                   "initialize",
                   JsonSerializer.SerializeToElement(@params, _parser.Options));
               _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient] initialize 请求已创建，id={request.Id}", ErrorSeverity.Info, nameof(InitializeAsync)));
               var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
               _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient] 收到 initialize 响应，id={response.Id}", ErrorSeverity.Info, nameof(InitializeAsync)));

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
            var initializeResponse = JsonSerializer.Deserialize<InitializeResponse>(response.Result!.Value.GetRawText(), _parser.Options);
            if (initializeResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse initialize response");
            }

            // 验证协议版本
            var serverVersionStr = initializeResponse.ProtocolVersion.ToString();
            var clientVersionStr = @params.ProtocolVersion.ToString();
            
            if (serverVersionStr != clientVersionStr)
            {
                throw new AcpException(
                    JsonRpcErrorCode.ProtocolVersionMismatch,
                    $"Protocol version mismatch. Expected: {clientVersionStr}, Actual: {serverVersionStr}");
            }

            // 存储 Agent 信息
            _agentInfo = initializeResponse.AgentInfo;
            _agentCapabilities = initializeResponse.AgentCapabilities;
            _capabilityManager.SetAgentCapabilities(initializeResponse.AgentCapabilities);
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

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/new",
                JsonSerializer.SerializeToElement(@params, typeof(SessionNewParams), _parser.Options));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var sessionNewResponse = JsonSerializer.Deserialize<SessionNewResponse>(response.Result!.Value.GetRawText(), _parser.Options);
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

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/load",
                JsonSerializer.SerializeToElement(@params, typeof(SessionLoadParams), _parser.Options));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            // 响应可能是 null（表示加载完成）
            return SessionLoadResponse.Completed;
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

            var promptStartUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/prompt",
                JsonSerializer.SerializeToElement(@params, typeof(SessionPromptParams), _parser.Options));

            JsonRpcResponse response;
            try
            {
                // session/prompt can be long-running (agents may take time before responding or streaming updates).
                response = await SendRequestAsync(request, cancellationToken, timeout: TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Some agents stream session/update events but never send a JSON-RPC response for session/prompt.
                // If we observed any incoming traffic after the prompt started, assume a streaming-only agent
                // and don't fail the UX.
                var lastRx = Interlocked.Read(ref _lastReceivedUnixMs);
                if (lastRx > promptStartUnixMs)
                {
                    _errorLogger.LogError(new ErrorLogEntry(
                        "PROMPT_NO_RESPONSE",
                        $"No JSON-RPC response for session/prompt (id={request.Id}). Received session traffic after prompt; assuming streaming-only agent.",
                        ErrorSeverity.Warning,
                        nameof(SendPromptAsync),
                        @params.SessionId));

                    return new SessionPromptResponse(StopReason.EndTurn);
                }

                throw;
            }

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var promptResponse = JsonSerializer.Deserialize<SessionPromptResponse>(response.Result!.Value.GetRawText(), _parser.Options);
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
                JsonSerializer.SerializeToElement(@params, typeof(SessionSetModeParams), _parser.Options));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var setModeResponse = JsonSerializer.Deserialize<SessionSetModeResponse>(response.Result!.Value.GetRawText(), _parser.Options);
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
                JsonSerializer.SerializeToElement(@params, typeof(SessionSetConfigOptionParams), _parser.Options));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var configResponse = JsonSerializer.Deserialize<SessionSetConfigOptionResponse>(response.Result!.Value.GetRawText(), _parser.Options);
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
                JsonSerializer.SerializeToElement(@params, typeof(SessionCancelParams), _parser.Options));

            await _transport.SendMessageAsync(
                _parser.SerializeMessage(notification),
                cancellationToken).ConfigureAwait(false);

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
                JsonSerializer.SerializeToElement(@params, typeof(AuthenticateParams), _parser.Options));

            var response = await SendRequestAsync(request, cancellationToken);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var authResponse = JsonSerializer.Deserialize<AuthenticateResponse>(response.Result!.Value.GetRawText(), _parser.Options);
            if (authResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse authenticate response");
            }

            return authResponse;
        }

        /// <summary>
        /// 响应权限请求。
        /// </summary>
        public async Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null)
        {
            return await TrySendPermissionOutcomeResponseAsync(messageId, outcome, optionId).ConfigureAwait(false);
        }

        /// <summary>
        /// 响应文件系统请求。
        /// </summary>
        public async Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
        {
            return await TrySendFileSystemResponseAsync(messageId, success, content, message).ConfigureAwait(false);
        }

        private async Task<bool> TrySendPermissionOutcomeResponseAsync(object messageId, string outcome, string? optionId)
        {
            // Only respond once per inbound request id.
            if (!_pendingInboundRequestMethods.TryRemove(messageId?.ToString() ?? string.Empty, out _))
            {
                return false;
            }

            // ACP schema: result = { outcome: { outcome: "selected"|"cancelled", optionId? } }
            object outcomePayload = string.IsNullOrWhiteSpace(optionId)
                ? new { outcome }
                : new { outcome, optionId };

            var response = new JsonRpcResponse(
                messageId,
                JsonSerializer.SerializeToElement(new { outcome = outcomePayload }, _parser.Options));
            return await SendResponseAsync(response).ConfigureAwait(false);
        }

        private async Task<bool> TrySendFileSystemResponseAsync(object messageId, bool success, string? content, string? message)
        {
            var idStr = messageId?.ToString() ?? string.Empty;
            if (!_pendingInboundRequestMethods.TryRemove(idStr, out var method))
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
            if (string.Equals(method, "fs/read_text_file", StringComparison.Ordinal))
            {
                result = JsonSerializer.SerializeToElement(new { content = content ?? string.Empty }, _parser.Options);
            }
            else
            {
                // fs/write_text_file returns null on success.
                result = JsonSerializer.SerializeToElement<object?>(null, _parser.Options);
            }

            return await SendResponseAsync(new JsonRpcResponse(messageId, result)).ConfigureAwait(false);
        }

        /// <summary>
        /// 断开与 Agent 的连接。
        /// </summary>
        public async Task<bool> DisconnectAsync()
        {
            _messageLoopCts?.Cancel();

            await _transport.DisconnectAsync();
            return true;
        }

        /// <summary>
        /// 发送请求并等待响应。
        /// </summary>
        
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

        private async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            var requestIdStr = request.Id?.ToString() ?? string.Empty;
            _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient.SendRequestAsync] 开始处理请求 id={requestIdStr}, method={request.Method}", ErrorSeverity.Info, nameof(SendRequestAsync)));
            var tcs = new TaskCompletionSource<JsonRpcResponse>();
            _pendingRequests[requestIdStr] = tcs;

            try
            {
                var effectiveTimeout = timeout ?? DefaultRequestTimeout;

                var json = _parser.SerializeMessage(request);
                _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient.SendRequestAsync] 请求已序列化，长度={json.Length}, json={json.Substring(0, Math.Min(200, json.Length))}...", ErrorSeverity.Info, nameof(SendRequestAsync)));
               _errorLogger.LogError(new ErrorLogEntry("DEBUG", "[AcpClient.SendRequestAsync] 准备调用 transport.SendMessageAsync...", ErrorSeverity.Info, nameof(SendRequestAsync)));
               var sendResult = await _transport.SendMessageAsync(json, cancellationToken).ConfigureAwait(false);
               _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient.SendRequestAsync] transport.SendMessageAsync 返回: {sendResult}", ErrorSeverity.Info, nameof(SendRequestAsync)));

               // 等待响应或超时
               using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
               {
                   cts.CancelAfter(effectiveTimeout);
                   _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient.SendRequestAsync] Waiting for response (timeout={effectiveTimeout.TotalSeconds:0}s)...", ErrorSeverity.Info, nameof(SendRequestAsync)));
                   var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                   _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient.SendRequestAsync] 等待完成，完成的任务: {(completedTask == tcs.Task ? "tcs.Task" : "timeout")}", ErrorSeverity.Info, nameof(SendRequestAsync)));
                    if (completedTask == tcs.Task)
                    {
                        return await tcs.Task.ConfigureAwait(false);
                    }
                    else
                    {
                        var lastRxMs = Interlocked.Read(ref _lastReceivedUnixMs);
                        var lastRxAge =
                            lastRxMs <= 0
                                ? "never"
                                : $"{TimeSpan.FromMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastRxMs).TotalSeconds:0.0}s ago";

                        var msg =
                            $"Request timed out (id={requestIdStr}, method={request.Method}, timeout={effectiveTimeout.TotalSeconds:0}s, lastRx={lastRxAge}). " +
                            "If the agent process doesn't speak ACP-over-stdio (e.g. it only supports MCP), it may never reply to ACP JSON-RPC requests.";
                        _errorLogger.LogError(new ErrorLogEntry("REQ_TIMEOUT", msg, ErrorSeverity.Warning, nameof(SendRequestAsync)));
                        throw new TimeoutException(msg);
                    }
                }
            }
            
            
            catch (Exception ex)
            {
                _errorLogger.LogError(new ErrorLogEntry("REQ_ERROR", $"[AcpClient.SendRequestAsync] Request {requestIdStr} failed: {ex.Message}", ErrorSeverity.Error, "SendRequestAsync", null, ex));
                _pendingRequests.TryRemove(requestIdStr, out _);
                throw;
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
            Interlocked.Exchange(ref _lastReceivedUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient.OnMessageReceived] 收到原始消息: {e.Message.Substring(0, Math.Min(200, e.Message.Length))}...", ErrorSeverity.Info, nameof(OnMessageReceived)));
            try
            {
                var message = _parser.ParseMessage(e.Message);
                _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient.OnMessageReceived] 消息解析成功，类型: {message.GetType().Name}", ErrorSeverity.Info, nameof(OnMessageReceived)));

                
                if (message is JsonRpcResponse response)
                {
                    var responseIdStr = response.Id?.ToString() ?? string.Empty;
                    _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient.OnMessageReceived] 收到响应，id={responseIdStr}, 是否有错误: {response.IsError}", ErrorSeverity.Info, nameof(OnMessageReceived)));
                    // 匹配 pending 请求
                    if (_pendingRequests.TryRemove(responseIdStr, out var tcs))
                    {
                        _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient.OnMessageReceived] 找到匹配的 pending 请求，设置结果...", ErrorSeverity.Info, nameof(OnMessageReceived)));
                        tcs.TrySetResult(response);
                        _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient.OnMessageReceived] TaskCompletionSource 已设置结果", ErrorSeverity.Info, nameof(OnMessageReceived)));
                    }
                    else
                    {
                        _errorLogger.LogError(new ErrorLogEntry("DEBUG", $"[AcpClient.OnMessageReceived] 未找到匹配的 pending 请求 id={responseIdStr}", ErrorSeverity.Warning, nameof(OnMessageReceived)));
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
                        _pendingInboundRequestMethods[requestIdStr] = request.Method;
                        ScheduleInboundRequestTimeout(request.Id, TimeSpan.FromSeconds(30), defaultKind: "permission");
                    }
                    HandlePermissionRequest(request);
                    break;
                case "fs/read_text_file":
                case "fs/write_text_file":
                    if (!string.IsNullOrWhiteSpace(requestIdStr))
                    {
                        _pendingInboundRequestMethods[requestIdStr] = request.Method;
                        ScheduleInboundRequestTimeout(request.Id, TimeSpan.FromSeconds(30), defaultKind: "fs");
                    }
                    HandleFileSystemRequest(request);
                    break;
                case "terminal/create":
                case "terminal/output":
                case "terminal/wait_for_exit":
                case "terminal/kill":
                case "terminal/release":
                    if (!string.IsNullOrWhiteSpace(requestIdStr))
                    {
                        _pendingInboundRequestMethods[requestIdStr] = request.Method;
                        ScheduleInboundRequestTimeout(request.Id, TimeSpan.FromSeconds(30), defaultKind: "terminal");
                    }
                    HandleTerminalRequest(request);
                    break;
                default:
                    // Best-effort: respond with "method not found" so the agent doesn't hang waiting.
                    _pendingInboundRequestMethods.TryRemove(request.Id?.ToString() ?? string.Empty, out _);
                    _ = SendResponseAsync(new JsonRpcResponse(
                        request.Id,
                        JsonRpcError.CreateMethodNotFound(request.Method)));
                    break;
            }
        }

        private void ScheduleInboundRequestTimeout(object messageId, TimeSpan timeout, string defaultKind)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(timeout).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                var idStr = messageId?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(idStr))
                {
                    return;
                }

                if (defaultKind == "permission")
                {
                    await TrySendPermissionOutcomeResponseAsync(messageId, "cancelled", null).ConfigureAwait(false);
                    return;
                }

                // For other tool requests, return a JSON-RPC error so the agent can continue.
                if (_pendingInboundRequestMethods.TryRemove(idStr, out _))
                {
                    var error = new JsonRpcError(
                        JsonRpcErrorCode.CapabilityNotSupported,
                        $"Client did not respond to {defaultKind} request in time.");
                    await SendResponseAsync(new JsonRpcResponse(messageId, error)).ConfigureAwait(false);
                }
            });
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

                var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(notification.Params.Value.GetRawText(), _parser.Options);
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
                    _pendingInboundRequestMethods.TryRemove(request.Id?.ToString() ?? string.Empty, out _);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp))
                {
                    _pendingInboundRequestMethods.TryRemove(request.Id?.ToString() ?? string.Empty, out _);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
                var toolCall = rawParams.TryGetProperty("toolCall", out var toolCallProp) ? toolCallProp : default;
                var optionsProp = rawParams.TryGetProperty("options", out var options) ? options : default;

                var optionsList = new List<SalmonEgg.Domain.Services.Security.PermissionOption>();
                if (optionsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var option in optionsProp.EnumerateArray())
                    {
                        var optionId = option.TryGetProperty("optionId", out var id) ? id.GetString() : "";
                        var name = option.TryGetProperty("name", out var n) ? n.GetString() : "";
                        var kind = option.TryGetProperty("kind", out var k) ? k.GetString() : "";
                        optionsList.Add(new SalmonEgg.Domain.Services.Security.PermissionOption(optionId, name, kind));
                    }
                }

                var permissionResponseFunc = new Func<string, string?, Task>((outcome, optionId) =>
                    RespondToPermissionRequestAsync(request.Id, outcome, optionId));

                var eventArgs = new PermissionRequestEventArgs(
                    request.Id,
                    sessionId,
                    toolCall,
                    optionsList,
                    permissionResponseFunc);

                if (PermissionRequestReceived == null)
                {
                    // No UI hooked up; cancel to avoid deadlock.
                    _ = RespondToPermissionRequestAsync(request.Id, "cancelled", null);
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
                    _pendingInboundRequestMethods.TryRemove(request.Id?.ToString() ?? string.Empty, out _);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp) ||
                    !rawParams.TryGetProperty("path", out var pathProp))
                {
                    _pendingInboundRequestMethods.TryRemove(request.Id?.ToString() ?? string.Empty, out _);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId or path")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
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
                RespondToFileSystemRequestAsync(request.Id, success, respContent, respMessage));

                var eventArgs = new FileSystemRequestEventArgs(
                request.Id,
                sessionId,
                operation,
                path,
                encoding,
                content,
                fileSystemResponseFunc);

                if (FileSystemRequestReceived == null)
                {
                    // No UI hooked up; deny to avoid deadlock.
                    _ = RespondToFileSystemRequestAsync(request.Id, success: false, content: null, message: "File system requests are not supported.");
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
        /// 处理终端请求。
        /// </summary>
        private void HandleTerminalRequest(JsonRpcRequest request)
        {
            try
            {
                if (!request.Params.HasValue)
                {
                    _pendingInboundRequestMethods.TryRemove(request.Id?.ToString() ?? string.Empty, out _);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp))
                {
                    _pendingInboundRequestMethods.TryRemove(request.Id?.ToString() ?? string.Empty, out _);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
                string? terminalId = null;
                if (rawParams.TryGetProperty("terminalId", out var terminalIdProp))
                {
                    terminalId = terminalIdProp.GetString();
                }

                var terminalResponseFunc = new Func<object, Task<bool>>(async (result) =>
                {
                    _pendingInboundRequestMethods.TryRemove(request.Id?.ToString() ?? string.Empty, out _);
                    return await SendResponseAsync(new JsonRpcResponse(request.Id, JsonSerializer.SerializeToElement(result, _parser.Options))).ConfigureAwait(false);
                });

                var eventArgs = new TerminalRequestEventArgs(
                    request.Id,
                    sessionId,
                    terminalId,
                    request.Method,
                    rawParams,
                    terminalResponseFunc);

                if (TerminalRequestReceived == null)
                {
                    // No UI hooked up; deny to avoid deadlock.
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateMethodNotFound(request.Method)));
                    return;
                }

                TerminalRequestReceived.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process terminal request: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理传输错误事件。
        /// </summary>
        private void OnTransportError(object? sender, TransportErrorEventArgs e)
        {
            OnErrorOccurred(e.ErrorMessage);
        }

        /// <summary>
        /// 触发错误事件。
        /// </summary>
        private void OnErrorOccurred(string errorMessage)
        {
            _errorLogger.LogError(new ErrorLogEntry("CLIENT_ERROR", errorMessage, ErrorSeverity.Error));
            ErrorOccurred?.Invoke(this, errorMessage);
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
        /// <summary>
        /// Lists all available sessions.
        /// </summary>
        public async Task<ListSessionsResponse> ListSessionsAsync(ListSessionsParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/list",
                JsonSerializer.SerializeToElement(@params, _parser.Options));
            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            var validationResult = _validator.ValidateResponse(response);
            if (!validationResult.IsValid)
            {
                throw new AcpException(JsonRpcErrorCode.InvalidRequest, $"Response validation failed: {string.Join("; ", validationResult.Errors)}");
            }

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var result = JsonSerializer.Deserialize<ListSessionsResponse>(response.Result!.Value.GetRawText(), _parser.Options);
            if (result == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to deserialize ListSessionsResponse");
            }

            return result;
        }

        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                throw new AcpException(JsonRpcErrorCode.NotInitialized, "ACP client is not initialized. Call InitializeAsync first.");
            }
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
            _transport.DisconnectAsync().Wait();
            _transport.MessageReceived -= OnMessageReceived;
            _transport.ErrorOccurred -= OnTransportError;
            GC.SuppressFinalize(this);
        }
    }
}
