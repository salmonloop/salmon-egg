using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using UnoAcpClient.Domain.Exceptions;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Domain.Services;

namespace UnoAcpClient.Infrastructure.Network
{
    /// <summary>
    /// 实现连接管理器，负责管理与 ACP 服务器的连接、消息发送和接收
    /// </summary>
    public class ConnectionManager : IConnectionManager, IDisposable
    {
        private readonly IAcpProtocolService _protocolService;
        private readonly ILogger _logger;
        private readonly Func<TransportType, ITransport> _transportFactory;
        
        private ITransport _transport;
        private readonly Subject<AcpMessage> _incomingMessages;
        private readonly BehaviorSubject<ConnectionState> _connectionStateChanges;
        private readonly Dictionary<string, TaskCompletionSource<AcpMessage>> _pendingRequests;
        private IDisposable _messageSubscription;
        private IDisposable _stateSubscription;
        private ServerConfiguration _currentConfig;
        private bool _disposed;

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

                // 建立连接
                await _transport.ConnectAsync(config.ServerUrl, ct);

                // 发送初始化消息
                await SendInitializeMessageAsync(ct);

                // 更新状态为已连接
                var connectedState = new ConnectionState
                {
                    Status = ConnectionStatus.Connected,
                    ServerUrl = config.ServerUrl,
                    ConnectedAt = DateTime.UtcNow
                };
                _connectionStateChanges.OnNext(connectedState);

                _logger.Information("成功连接到服务器: {ServerUrl}", config.ServerUrl);
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
                _logger.Error(ex, "连接时发生意外错误");
                UpdateConnectionState(ConnectionStatus.Error, config.ServerUrl, "意外错误");
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
                _logger.Information("开始断开连接");

                if (_transport != null)
                {
                    // 取消订阅
                    UnsubscribeFromTransport();

                    // 断开传输层连接
                    await _transport.DisconnectAsync();
                    _transport = null;
                }

                // 清理待处理的请求
                ClearPendingRequests("连接已断开");

                // 更新状态
                UpdateConnectionState(ConnectionStatus.Disconnected, _currentConfig?.ServerUrl ?? string.Empty);

                _logger.Information("已断开连接");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "断开连接时发生错误");
                throw;
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
                    var error = "未连接到服务器";
                    _logger.Warning("尝试发送消息但未连接: {MessageId}", message.Id);
                    return SendResult.Failure(error);
                }

                // 验证消息
                if (!_protocolService.ValidateMessage(message))
                {
                    var error = "消息验证失败";
                    _logger.Warning("消息验证失败: {MessageId}", message.Id);
                    return SendResult.Failure(error);
                }

                // 序列化消息
                string json = _protocolService.SerializeMessage(message);

                // 发送消息
                await _transport.SendAsync(json, ct);

                _logger.Debug("已发送消息: {MessageId}, 类型: {Type}", message.Id, message.Type);
                return SendResult.Success(message.Id);
            }
            catch (AcpProtocolException ex)
            {
                _logger.Error(ex, "协议错误: {ErrorCode}", ex.ErrorCode);
                return SendResult.Failure($"协议错误: {ex.Message}");
            }
            catch (OperationCanceledException)
            {
                _logger.Information("发送消息操作被取消: {MessageId}", message.Id);
                return SendResult.Failure("发送操作被取消");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "发送消息时发生错误: {MessageId}", message.Id);
                return SendResult.Failure($"发送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 验证服务器配置
        /// </summary>
        private string ValidateConfiguration(ServerConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                return "服务器 URL 不能为空";
            }

            if (!Uri.TryCreate(config.ServerUrl, UriKind.Absolute, out var uri))
            {
                return "服务器 URL 格式无效";
            }

            // 验证传输类型对应的 URL 协议
            if (config.Transport == TransportType.WebSocket)
            {
                if (uri.Scheme != "ws" && uri.Scheme != "wss")
                {
                    return "WebSocket 传输需要 ws:// 或 wss:// 协议";
                }
            }
            else if (config.Transport == TransportType.HttpSse)
            {
                if (uri.Scheme != "http" && uri.Scheme != "https")
                {
                    return "HTTP SSE 传输需要 http:// 或 https:// 协议";
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
                    onError: ex => _logger.Error(ex, "传输层状态流发生错误"));
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
                _logger.Debug("收到消息: {Json}", json);

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
                _logger.Error(ex, "解析消息失败: {ErrorCode}", ex.ErrorCode);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "处理消息时发生错误");
            }
        }

        /// <summary>
        /// 处理消息流错误
        /// </summary>
        private void OnMessageError(Exception ex)
        {
            _logger.Error(ex, "消息流发生错误");
            UpdateConnectionState(ConnectionStatus.Error, _currentConfig?.ServerUrl ?? string.Empty, ex.Message);
        }

        /// <summary>
        /// 处理消息流完成
        /// </summary>
        private void OnMessageCompleted()
        {
            _logger.Information("消息流已完成");
            UpdateConnectionState(ConnectionStatus.Disconnected, _currentConfig?.ServerUrl ?? string.Empty);
        }

        /// <summary>
        /// 处理传输层状态变化
        /// </summary>
        private void OnTransportStateChanged(TransportState state)
        {
            _logger.Debug("传输层状态变化: {State}", state);

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
        /// 更新连接状态
        /// </summary>
        private void UpdateConnectionState(ConnectionStatus status, string serverUrl, string errorMessage = null)
        {
            var state = new ConnectionState
            {
                Status = status,
                ServerUrl = serverUrl,
                ErrorMessage = errorMessage ?? string.Empty,
                ConnectedAt = status == ConnectionStatus.Connected ? DateTime.UtcNow : (DateTime?)null
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

            _logger.Information("已发送初始化消息: {MessageId}", initMessage.Id);
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
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            UnsubscribeFromTransport();
            _incomingMessages?.Dispose();
            _connectionStateChanges?.Dispose();
            ClearPendingRequests("ConnectionManager 已释放");

            _disposed = true;
        }
    }
}
