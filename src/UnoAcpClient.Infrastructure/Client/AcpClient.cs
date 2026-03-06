using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UnoAcpClient.Domain.Interfaces;
using UnoAcpClient.Domain.Interfaces.Transport;
using UnoAcpClient.Domain.Models.Content;
using UnoAcpClient.Domain.Models.JsonRpc;
using UnoAcpClient.Domain.Models.Plan;
using UnoAcpClient.Domain.Models.Protocol;
using UnoAcpClient.Domain.Models.Session;
using UnoAcpClient.Domain.Models.Tool;
using UnoAcpClient.Domain.Services;
using UnoAcpClient.Domain.Services.Security;
using UnoAcpClient.Infrastructure.Serialization;
namespace UnoAcpClient.Infrastructure.Client
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

        private readonly ConcurrentDictionary<object, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();
        private readonly object _lock = new();
        private bool _disposed;
        private CancellationTokenSource? _messageLoopCts;

        private bool _isInitialized;
        private AgentInfo? _agentInfo;
        private AgentCapabilities? _agentCapabilities;
        private long _nextMessageId;

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
            IMessageValidator? validator = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _parser = parser ?? new MessageParser();
            _validator = validator ?? new MessageValidator();
            _capabilityManager = new Services.CapabilityManager();
            _sessionManager = new Services.SessionManager();
            _pathValidator = new Services.Security.PathValidator();
            _permissionManager = new Services.Security.PermissionManager();

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

            // 发送 initialize 请求
            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "initialize",
                JsonSerializer.SerializeToElement(@params, typeof(InitializeParams), _parser.GetType().Assembly.GetType("System.Text.Json.JsonSerializerOptions") != null ? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase } : null));

            var response = await SendRequestAsync(request, cancellationToken);

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
            var initializeResponse = JsonSerializer.Deserialize<InitializeResponse>(response.Result!.Value.GetRawText());
            if (initializeResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse initialize response");
            }

            // 验证协议版本
            if (initializeResponse.ProtocolVersion != @params.ProtocolVersion)
            {
                throw new AcpException(
                    JsonRpcErrorCode.ProtocolVersionMismatch,
                    $"Protocol version mismatch. Expected: {@params.ProtocolVersion}, Actual: {initializeResponse.ProtocolVersion}");
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
                JsonSerializer.SerializeToElement(@params, typeof(SessionNewParams)));

            var response = await SendRequestAsync(request, cancellationToken);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var sessionNewResponse = JsonSerializer.Deserialize<SessionNewResponse>(response.Result!.Value.GetRawText());
            if (sessionNewResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/new response");
            }

            // 创建会话记录
            await _sessionManager.CreateSessionAsync(sessionNewResponse.SessionId, @params.Cwd);

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
                JsonSerializer.SerializeToElement(@params, typeof(SessionLoadParams)));

            var response = await SendRequestAsync(request, cancellationToken);

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

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/prompt",
                JsonSerializer.SerializeToElement(@params, typeof(SessionPromptParams)));

            var response = await SendRequestAsync(request, cancellationToken);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var promptResponse = JsonSerializer.Deserialize<SessionPromptResponse>(response.Result!.Value.GetRawText());
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
                JsonSerializer.SerializeToElement(@params, typeof(SessionSetModeParams)));

            var response = await SendRequestAsync(request, cancellationToken);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var setModeResponse = JsonSerializer.Deserialize<SessionSetModeResponse>(response.Result!.Value.GetRawText());
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
                JsonSerializer.SerializeToElement(@params, typeof(SessionSetConfigOptionParams)));

            var response = await SendRequestAsync(request, cancellationToken);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var configResponse = JsonSerializer.Deserialize<SessionSetConfigOptionResponse>(response.Result!.Value.GetRawText());
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

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/cancel",
                JsonSerializer.SerializeToElement(@params, typeof(SessionCancelParams)));

            var response = await SendRequestAsync(request, cancellationToken);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var cancelResponse = JsonSerializer.Deserialize<SessionCancelResponse>(response.Result!.Value.GetRawText());
            if (cancelResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/cancel response");
            }

            // 更新会话状态
            await _sessionManager.CancelSessionAsync(@params.SessionId, @params.Reason);

            return cancelResponse;
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
                JsonSerializer.SerializeToElement(@params, typeof(AuthenticateParams)));

            var response = await SendRequestAsync(request, cancellationToken);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var authResponse = JsonSerializer.Deserialize<AuthenticateResponse>(response.Result!.Value.GetRawText());
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
            var response = new JsonRpcResponse(messageId, JsonSerializer.SerializeToElement(new { outcome, optionId }));
            return await SendResponseAsync(response);
        }

        /// <summary>
        /// 响应文件系统请求。
        /// </summary>
        public async Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
        {
            var result = new
            {
                success,
                content,
                message
            };

            var response = new JsonRpcResponse(messageId, JsonSerializer.SerializeToElement(result));
            return await SendResponseAsync(response);
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
        private async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<JsonRpcResponse>();
            _pendingRequests[request.Id] = tcs;

            try
            {
                var json = _parser.SerializeMessage(request);
                await _transport.SendMessageAsync(json, cancellationToken);

                // 等待响应或超时
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(30));
                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
                    if (completedTask == tcs.Task)
                    {
                        return await tcs.Task;
                    }
                    else
                    {
                        throw new TimeoutException("Request timed out");
                    }
                }
            }
            catch
            {
                _pendingRequests.TryRemove(request.Id, out _);
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
                await _transport.SendMessageAsync(json);
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
                    // 匹配 pending 请求
                    if (_pendingRequests.TryRemove(response.Id, out var tcs))
                    {
                        tcs.TrySetResult(response);
                    }
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
                case "session/request_permission":
                    HandlePermissionRequest(notification);
                    break;
                case "fs/read_text_file":
                case "fs/write_text_file":
                    HandleFileSystemRequest(notification);
                    break;
                default:
                    // 未知通知类型
                    break;
            }
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

                var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(notification.Params.Value.GetRawText());
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
        private void HandlePermissionRequest(JsonRpcNotification notification)
        {
            try
            {
                if (!notification.Params.HasValue)
                {
                    return;
                }

                var rawParams = notification.Params.Value;
                var sessionId = rawParams.GetProperty("sessionId").GetString() ?? "";
                var toolCall = rawParams.TryGetProperty("toolCall", out var toolCallProp) ? toolCallProp : default;
                var optionsProp = rawParams.TryGetProperty("options", out var options) ? options : default;

                var optionsList = new List<UnoAcpClient.Domain.Services.Security.PermissionOption>();
                if (optionsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var option in optionsProp.EnumerateArray())
                    {
                        var optionId = option.TryGetProperty("optionId", out var id) ? id.GetString() : "";
                        var name = option.TryGetProperty("name", out var n) ? n.GetString() : "";
                        var kind = option.TryGetProperty("kind", out var k) ? k.GetString() : "";
                        optionsList.Add(new UnoAcpClient.Domain.Services.Security.PermissionOption(optionId, name, kind));
                    }
                }

                var permissionResponseFunc = new Func<string, string?, Task>((outcome, optionId) =>
                    RespondToPermissionRequestAsync(notification.Id!, outcome, optionId));

                var eventArgs = new PermissionRequestEventArgs(
                    notification.Id!,
                    sessionId,
                    toolCall,
                    optionsList,
                    permissionResponseFunc);

                PermissionRequestReceived?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process permission request: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理文件系统请求通知。
        /// </summary>
        private void HandleFileSystemRequest(JsonRpcNotification notification)
        {
            try
            {
                if (!notification.Params.HasValue)
                {
                    return;
                }

                var rawParams = notification.Params.Value;
                var sessionId = rawParams.GetProperty("sessionId").GetString() ?? "";
                var operation = rawParams.GetProperty("operation").GetString() ?? "";
                var path = rawParams.GetProperty("path").GetString() ?? "";
                var encoding = rawParams.TryGetProperty("encoding", out var enc) ? enc.GetString() : null;
                var content = rawParams.TryGetProperty("content", out var cont) ? cont.GetString() : null;

                var fileSystemResponseFunc = new Func<bool, string?, string?, Task>((success, respContent, respMessage) =>
                RespondToFileSystemRequestAsync(notification.Id!, success, respContent, respMessage));

                var eventArgs = new FileSystemRequestEventArgs(
                notification.Id!,
                sessionId,
                operation,
                path,
                encoding,
                content,
                fileSystemResponseFunc);

                FileSystemRequestReceived?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process file system request: {ex.Message}");
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
