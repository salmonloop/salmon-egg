using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Polly;
using Polly.Retry;
using Serilog;
using SalmonEgg.Domain.Exceptions;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Network
{
    /// <summary>
    /// 实现连接管理器，负责管理与 ACP 服务器的连接、消息发送和接收
    /// </summary>
    public class ConnectionManager : IConnectionManager, IDisposable
    {
        private readonly IAcpProtocolService _protocolService;
        private readonly ILogger _logger;
        private readonly Func<TransportType, ITransport> _transportFactory;
        private readonly AsyncRetryPolicy _retryPolicy;
        
        private ITransport? _transport;
        private readonly Subject<AcpMessage> _incomingMessages;
        private readonly BehaviorSubject<ConnectionState> _connectionStateChanges;
        private readonly Dictionary<string, TaskCompletionSource<AcpMessage>> _pendingRequests;
        private IDisposable? _messageSubscription;
        private IDisposable? _stateSubscription;
        private ServerConfiguration? _currentConfig;
        private System.Timers.Timer? _heartbeatTimer;
        private bool _disposed;
        private bool _isReconnecting;
        private bool _isManualDisconnect;

        /// <summary>
        /// 获取传入消息的可观察流
        /// </summary>
        public IObservable<AcpMessage> IncomingMessages => _incomingMessages.AsObservable();

        /// <summary>
        /// 获取连接状态变化的可观察流
        /// </summary>
        public IObservable<ConnectionState> ConnectionStateChanges => _connectionStateChanges.AsObservable();

        /// <summary>
        /// 初始化 ConnectionManager 实例
        /// </summary>
        /// <param name="protocolService">ACP 协议服务</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="transportFactory">传输层工厂函数</param>
        public ConnectionManager(
            IAcpProtocolService protocolService,
            ILogger logger,
            Func<TransportType, ITransport> transportFactory)
        {
            _protocolService = protocolService ?? throw new ArgumentNullException(nameof(protocolService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            
            _incomingMessages = new Subject<AcpMessage>();
            _connectionStateChanges = new BehaviorSubject<ConnectionState>(new ConnectionState
            {
                Status = ConnectionStatus.Disconnected
            });
            _pendingRequests = new Dictionary<string, TaskCompletionSource<AcpMessage>>();
            
            // 配置重试策略：最多重试 3 次，使用指数退避
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.Warning(
                            "连接尝试 {RetryCount}/3 失败: {Error}. 将在 {Delay} 秒后重试",
                            retryCount, exception.Message, timeSpan.TotalSeconds);
                        
                        // 更新连接状态为重连中
                        UpdateConnectionState(
                            ConnectionStatus.Reconnecting, 
                            _currentConfig?.ServerUrl ?? string.Empty,
                            $"重试 {retryCount}/3",
                            retryCount);
                    });
        }

        /// <summary>
        /// 异步连接到 ACP 服务器
        /// </summary>
        public async Task<ConnectionResult> ConnectAsync(ServerConfiguration config, CancellationToken ct)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            try
            {
                _logger.Information("开始连接到服务器: {ServerUrl}, 传输类型: {Transport}", 
                    config.ServerUrl, config.Transport);

                // 验证配置
                var validationError = ValidateConfiguration(config);
                if (validationError != null)
                {
                    _logger.Warning("配置验证失败: {Error}", validationError);
                    UpdateConnectionState(ConnectionStatus.Error, config.ServerUrl, validationError);
                    return ConnectionResult.Failure(validationError);
                }

                // 更新状态为连接中
                UpdateConnectionState(ConnectionStatus.Connecting, config.ServerUrl);

                // 选择并创建传输层
                _transport = _transportFactory(config.Transport);
                _currentConfig = config;

                // 订阅传输层的消息和状态变化
                SubscribeToTransport();

                // 使用重试策略建立连接
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await _transport.ConnectAsync(config.ServerUrl, ct);
                });

                // 发送初始化消息
                await SendInitializeMessageAsync(ct);

                // 启动心跳定时器
                StartHeartbeat(config.HeartbeatInterval);

                // 更新状态为已连接
                UpdateConnectionState(ConnectionStatus.Connected, config.ServerUrl);

                _logger.Information("成功连接到服务器: {ServerUrl}", config.ServerUrl);
                
                // 获取当前连接状态并返回
                var connectedState = _connectionStateChanges.Value;
                return ConnectionResult.Success(connectedState);
            }
            catch (OperationCanceledException)
            {
                _logger.Information("连接操作被取消");
                UpdateConnectionState(ConnectionStatus.Disconnected, config.ServerUrl, "连接被取消");
                return ConnectionResult.Failure("连接操作被取消");
            }
            catch (ConnectionException ex)
            {
                _logger.Error(ex, "连接失败: {ErrorType}", ex.ErrorType);
                UpdateConnectionState(ConnectionStatus.Error, config.ServerUrl, ex.Message);
                return ConnectionResult.Failure($"连接失败: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "连接时发生意外错误，已达到最大重试次数");
                UpdateConnectionState(ConnectionStatus.Error, config.ServerUrl, "连接失败，已达到最大重试次数");
                return ConnectionResult.Failure($"连接失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 异步断开与服务器的连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                _logger.Information("Disconnecting from server");
                
                // 设置手动断开标志，防止自动重连
                _isManualDisconnect = true;

                // 停止心跳定时器
                StopHeartbeat();

                if (_transport != null)
                {
                    // 取消订阅
                    UnsubscribeFromTransport();

                    // 断开传输层连接
                    await _transport.DisconnectAsync();
                    _transport = null;
                }

                // 清理待处理的请求
                ClearPendingRequests("Connection disconnected manually");

                // 更新状态
                UpdateConnectionState(ConnectionStatus.Disconnected, _currentConfig?.ServerUrl ?? string.Empty);
                
                // 清除配置信息
                _currentConfig = null;

                _logger.Information("Connection disconnected successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Connection disconnect failed");
                throw;
            }
            finally
            {
                _isManualDisconnect = false;
            }
        }

        /// <summary>
        /// 异步发送 ACP 消息到服务器
        /// </summary>
        public async Task<SendResult> SendMessageAsync(AcpMessage message, CancellationToken ct)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            try
            {
                // 检查连接状态
                var currentState = _connectionStateChanges.Value;
                if (currentState.Status != ConnectionStatus.Connected)
                {
                    var error = "Not connected to server";
                    _logger.Warning("Attempt to send message but not connected");
                    return SendResult.Failure(error);
                }

                // 验证消息
                if (!_protocolService.ValidateMessage(message))
                {
                    var error = "Message validation failed";
                    _logger.Warning("Message validation failed: {MessageId}", message.Id);
                    return SendResult.Failure(error);
                }

                // 序列化消息
                string json = _protocolService.SerializeMessage(message);

                // 发送消息
                await _transport.SendAsync(json, ct);

                _logger.Debug("Message sent successfully: {MessageId}, Type: {Type}", message.Id, message.Type);
                return SendResult.Success(message.Id);
            }
            catch (AcpProtocolException ex)
            {
                _logger.Error(ex, "Protocol error: {ErrorCode}", ex.ErrorCode);
                return SendResult.Failure($"Protocol error: {ex.Message}");
            }
            catch (OperationCanceledException)
            {
                _logger.Information("Operation to send message canceled");
                return SendResult.Failure("Operation canceled by user");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Send message failed");
                return SendResult.Failure($"Send message failed with error: {ex.Message}");
            }
        }

        /// <summary>
        /// 验证服务器配置
        /// </summary>
        private string ValidateConfiguration(ServerConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                return "Server URL is required";
            }

            if (!Uri.TryCreate(config.ServerUrl, UriKind.Absolute, out var uri))
            {
                return "Server URL format is invalid";
            }

            // 验证传输类型对应的 URL 协议
            if (config.Transport == TransportType.WebSocket)
            {
                if (uri.Scheme != "ws" && uri.Scheme != "wss")
                {
                    return "WebSocket transport requires ws:// or wss:// protocol";
                }
            }
            else if (config.Transport == TransportType.HttpSse)
            {
                if (uri.Scheme != "http" && uri.Scheme != "https")
                {
                    return "HTTP SSE transport requires http:// or https:// protocol";
                }
            }

            return null;
        }

        /// <summary>
        /// 订阅传输层的消息和状态变化
        /// </summary>
        private void SubscribeToTransport()
        {
            if (_transport == null)
            {
                return;
            }

            // 订阅传入消息
            _messageSubscription = _transport.Messages
                .Subscribe(
                    onNext: OnMessageReceived,
                    onError: OnMessageError,
                    onCompleted: OnMessageCompleted);

            // 订阅状态变化
            _stateSubscription = _transport.StateChanges
                .Subscribe(
                    onNext: OnTransportStateChanged,
                    onError: ex => _logger.Error(ex, "Transport state change stream error"));
        }

        /// <summary>
        /// 取消订阅传输层
        /// </summary>
        private void UnsubscribeFromTransport()
        {
            _messageSubscription?.Dispose();
            _messageSubscription = null;

            _stateSubscription?.Dispose();
            _stateSubscription = null;
        }

        /// <summary>
        /// 处理接收到的消息
        /// </summary>
        private void OnMessageReceived(string json)
        {
            try
            {
                _logger.Debug("Received message from server: {Json}", json);

                // 解析消息
                var message = _protocolService.ParseMessage(json);

                // 如果是响应消息，匹配待处理的请求
                if (message.Type == "response" && _pendingRequests.TryGetValue(message.Id, out var tcs))
                {
                    _pendingRequests.Remove(message.Id);
                    tcs.SetResult(message);
                }

                // 发布到传入消息流
                _incomingMessages.OnNext(message);
            }
            catch (AcpProtocolException ex)
            {
                _logger.Error(ex, "Protocol error: {ErrorCode}", ex.ErrorCode);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Message processing error");
            }
        }

        /// <summary>
        /// 处理消息流错误
        /// </summary>
        private void OnMessageError(Exception ex)
        {
            _logger.Error(ex, "Message stream error");
            UpdateConnectionState(ConnectionStatus.Error, _currentConfig?.ServerUrl ?? string.Empty, ex.Message);
        }

        /// <summary>
        /// 处理消息流完成
        /// </summary>
        private void OnMessageCompleted()
        {
            _logger.Information("Message stream completed");
            UpdateConnectionState(ConnectionStatus.Disconnected, _currentConfig?.ServerUrl ?? string.Empty);
        }

        /// <summary>
        /// 处理传输层状态变化
        /// </summary>
        private void OnTransportStateChanged(TransportState state)
        {
            _logger.Debug("Transport state change: {State}", state);

            // 将传输层状态映射到连接状态
            ConnectionStatus connectionStatus;
            switch (state)
            {
                case TransportState.Connecting:
                    connectionStatus = ConnectionStatus.Connecting;
                    break;
                case TransportState.Connected:
                    connectionStatus = ConnectionStatus.Connected;
                    break;
                case TransportState.Disconnected:
                    connectionStatus = ConnectionStatus.Disconnected;
                    // 如果是意外断开（有配置信息且不是手动断开或正在重连），尝试自动重连
                    if (_currentConfig != null && !_isManualDisconnect && !_isReconnecting)
                    {
                        _logger.Information("Connection disconnected unexpectedly, preparing to reconnect");
                        _ = AutoReconnectAsync();
                    }
                    break;
                case TransportState.Error:
                    connectionStatus = ConnectionStatus.Error;
                    break;
                default:
                    return; // 忽略其他状态
            }

            UpdateConnectionState(connectionStatus, _currentConfig?.ServerUrl ?? string.Empty);
        }

        /// <summary>
        /// 自动重连到服务器
        /// </summary>
        private async Task AutoReconnectAsync()
        {
            if (_currentConfig == null)
            {
                _logger.Warning("No configuration information available to reconnect automatically");
                return;
            }

            if (_isReconnecting)
            {
                _logger.Debug("Already reconnecting, skipping this reconnect request");
                _logger.Debug("已经在重连中，跳过此次重连请求");
                return;
            }

            try
            {
                _isReconnecting = true;
                _logger.Information("Reconnecting to server automatically: {ServerUrl}", _currentConfig.ServerUrl);
                
                // 更新状态为重连中
                UpdateConnectionState(ConnectionStatus.Reconnecting, _currentConfig.ServerUrl, "正在自动重连");

                // 清理现有连接
                if (_transport != null)
                {
                    UnsubscribeFromTransport();
                    try
                    {
                        await _transport.DisconnectAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Error cleaning up existing connection");
                    }
                }

                // 创建新的传输层实例
                _transport = _transportFactory(_currentConfig.Transport);
                
                // 订阅传输层的消息和状态变化
                SubscribeToTransport();

                // 使用重试策略建立连接
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await _transport.ConnectAsync(_currentConfig.ServerUrl, CancellationToken.None);
                });

                // 发送初始化消息
                await SendInitializeMessageAsync(CancellationToken.None);

                // 重新启动心跳定时器
                StartHeartbeat(_currentConfig.HeartbeatInterval);

                // 更新状态为已连接
                UpdateConnectionState(ConnectionStatus.Connected, _currentConfig.ServerUrl);

                _logger.Information("Reconnection successful to server: {ServerUrl}", _currentConfig.ServerUrl);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Reconnection failed");
                UpdateConnectionState(
                    ConnectionStatus.Error, 
                    _currentConfig.ServerUrl, 
                    "Reconnection failed");
            }
            finally
            {
                _isReconnecting = false;
            }
        }

        /// <summary>
        /// 更新连接状态
        /// </summary>
        private void UpdateConnectionState(ConnectionStatus status, string serverUrl, string errorMessage = null, int retryCount = 0)
        {
            var state = new ConnectionState
            {
                Status = status,
                ServerUrl = serverUrl,
                ErrorMessage = errorMessage ?? string.Empty,
                ConnectedAt = status == ConnectionStatus.Connected ? DateTime.UtcNow : (DateTime?)null,
                RetryCount = retryCount
            };

            _connectionStateChanges.OnNext(state);
        }

        /// <summary>
        /// 发送初始化消息
        /// </summary>
        private async Task SendInitializeMessageAsync(CancellationToken ct)
        {
            var initMessage = new AcpMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "initialize",
                ProtocolVersion = "1.0",
                Timestamp = DateTime.UtcNow
            };

            var json = _protocolService.SerializeMessage(initMessage);
            await _transport.SendAsync(json, ct);

            _logger.Information("Initialize message sent successfully to server: {MessageId}", initMessage.Id);
        }

        /// <summary>
        /// 清理待处理的请求
        /// </summary>
        private void ClearPendingRequests(string reason)
        {
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(new ConnectionException(reason, ConnectionErrorType.ServerError));
            }
            _pendingRequests.Clear();
        }

        /// <summary>
        /// 启动心跳定时器
        /// </summary>
        private void StartHeartbeat(int intervalSeconds)
        {
            // 停止现有的心跳定时器（如果有）
            StopHeartbeat();

            _logger.Debug("Heartbeat timer started with interval of: {Interval} seconds", intervalSeconds);

            _heartbeatTimer = new System.Timers.Timer(intervalSeconds * 1000);
            _heartbeatTimer.Elapsed += OnHeartbeatTimerElapsed;
            _heartbeatTimer.AutoReset = true;
            _heartbeatTimer.Start();
        }

        /// <summary>
        /// 停止心跳定时器
        /// </summary>
        private void StopHeartbeat()
        {
            if (_heartbeatTimer != null)
            {
                _logger.Debug("Heartbeat timer stopped");
                _heartbeatTimer.Stop();
                _heartbeatTimer.Elapsed -= OnHeartbeatTimerElapsed;
                _heartbeatTimer.Dispose();
                _heartbeatTimer = null;
            }
        }

        /// <summary>
        /// 心跳定时器触发事件处理
        /// </summary>
        private async void OnHeartbeatTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                // 检查连接状态
                var currentState = _connectionStateChanges.Value;
                if (currentState.Status != ConnectionStatus.Connected || _transport == null)
                {
                    _logger.Debug("Skip heartbeat: Not connected");
                    return;
                }

                // 创建心跳消息
                var heartbeatMessage = new AcpMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = "notification",
                    Method = "heartbeat",
                    Timestamp = DateTime.UtcNow
                };

                // 序列化并发送心跳消息
                var json = _protocolService.SerializeMessage(heartbeatMessage);
                await _transport.SendAsync(json, CancellationToken.None);

                _logger.Debug("Heartbeat message sent successfully to server: {MessageId}", heartbeatMessage.Id);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Heartbeat message send failed");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            StopHeartbeat();
            UnsubscribeFromTransport();
            _incomingMessages?.Dispose();
            _connectionStateChanges?.Dispose();
            ClearPendingRequests("ConnectionManager disposed");

            _disposed = true;
        }
    }
}
