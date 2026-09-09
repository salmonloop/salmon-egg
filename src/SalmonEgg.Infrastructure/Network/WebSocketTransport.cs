using System;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Serilog;
using SalmonEgg.Domain.Models;
using Websocket.Client;

namespace SalmonEgg.Infrastructure.Network
{
    /// <summary>
    /// WebSocket transport implementation using Websocket.Client library.
    /// Provides message streaming using Reactive Extensions.
    /// </summary>
    public class WebSocketTransport : ITransport, ITransportStateSource, IDisposable
    {
        private readonly ILogger _logger;
        private readonly ProxyConfig _proxyConfiguration;
        private readonly Func<Uri, ProxyConfig, IWebsocketClient> _clientFactory;
        private readonly ResolvedCredentialBinding? _credential;
        private readonly HttpMessageInvoker? _credentialInvoker;
        // Replaced on connect and cleared on teardown, both of which can run while a send is in
        // flight. Volatile so a send sees the current handle, and every path that uses it more than
        // once captures it into a local first so it cannot be swapped mid-decision.
        private volatile IWebsocketClient? _client;
        private IDisposable? _clientSubscriptions;
        private readonly Subject<string> _messagesSubject;
        private readonly BehaviorSubject<TransportStateChange> _stateSubject;
        private readonly TimeSpan _connectTimeout;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the WebSocketTransport class.
        /// </summary>
        /// <param name="logger">Logger instance for logging transport events.</param>
        public WebSocketTransport(
            ILogger logger,
            ProxyConfig? proxyConfiguration = null,
            TimeSpan? connectTimeout = null,
            ResolvedCredentialBinding? credential = null)
            : this(
                logger,
                proxyConfiguration,
                connectTimeout,
                (uri, proxy) => CreateClient(uri, proxy, connectTimeout ?? TimeSpan.FromSeconds(AcpConnectionTimeoutPolicy.DefaultSeconds)))
        {
            _credential = credential;
            if (credential is { HasHeader: true })
            {
                _credentialInvoker = CreateCredentialInvoker(_proxyConfiguration);
                _clientFactory = CreateCredentialClient;
            }
        }

        internal WebSocketTransport(
            ILogger logger,
            ProxyConfig? proxyConfiguration,
            TimeSpan? connectTimeout,
            Func<Uri, ProxyConfig, IWebsocketClient> clientFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _proxyConfiguration = CloneProxyConfiguration(proxyConfiguration);
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _messagesSubject = new Subject<string>();
            _stateSubject = new BehaviorSubject<TransportStateChange>(new(TransportState.Disconnected));
            _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(AcpConnectionTimeoutPolicy.DefaultSeconds);
        }

        public TimeSpan ConnectTimeout => _connectTimeout;

        internal ProxyConfig ProxyConfiguration => CloneProxyConfiguration(_proxyConfiguration);

        /// <inheritdoc />
        public IObservable<string> Messages => _messagesSubject;

        /// <inheritdoc />
        public IObservable<TransportState> StateChanges => _stateSubject.Select(static change => change.State);

        IObservable<TransportStateChange> ITransportStateSource.StateTransitions => _stateSubject;

        /// <inheritdoc />
        public async Task ConnectAsync(string url, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            }

            BoundCredentialHeader.EnsureEndpoint(_credential, new Uri(url, UriKind.Absolute));

            var existingClient = _client;
            if (existingClient != null && existingClient.IsRunning)
            {
                _logger.Warning("WebSocket is already connected to {Url}", url);
                return;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                PublishState(TransportState.Connecting);
                _logger.Information(
                    "Connecting to WebSocket server at {Url}. proxyMode={ProxyMode} timeoutSeconds={TimeoutSeconds}",
                    url,
                    _proxyConfiguration.Mode,
                    _connectTimeout.TotalSeconds);

                DisposeClient();

                var uri = new Uri(url);
                _client = _clientFactory(uri, _proxyConfiguration);

                _client.ReconnectTimeout = null;
                SubscribeToWebSocketEvents();

                var connectionSignal = new TaskCompletionSource<DisconnectionInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var connectionStateProjectedByEvent = false;
                using var connectionSubscriptions = new CompositeDisposable(
                    _client.ReconnectionHappened.Subscribe(_ =>
                    {
                        connectionStateProjectedByEvent = true;
                        connectionSignal.TrySetResult(null);
                    }),
                    _client.DisconnectionHappened.Subscribe(info =>
                    {
                        if (info.Type == DisconnectionType.Exit || info.Type == DisconnectionType.ByUser)
                        {
                            return;
                        }

                        connectionSignal.TrySetResult(info);
                    }));

                await _client.Start();
                _logger.Information(
                    "WebSocket Start returned for {Url}. elapsedMs={ElapsedMs} isRunning={IsRunning}",
                    url,
                    stopwatch.ElapsedMilliseconds,
                    _client.IsRunning);

                if (_client.IsRunning)
                {
                    connectionSignal.TrySetResult(null);
                }

                var completedTask = await Task.WhenAny(connectionSignal.Task, Task.Delay(_connectTimeout, ct));
                if (completedTask != connectionSignal.Task)
                {
                    if (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("Connection cancelled by user", ct);
                    }

                    throw new TimeoutException($"Failed to connect to {url} within {_connectTimeout.TotalSeconds} seconds");
                }

                var disconnection = await connectionSignal.Task;
                if (disconnection != null)
                {
                    throw CreateConnectFailure(url, disconnection);
                }

                if (!connectionStateProjectedByEvent)
                {
                    PublishState(TransportState.Connected);
                }

                _logger.Information("Successfully connected to WebSocket server at {Url}", url);
            }
            catch (Exception ex)
            {
                DisposeClient();
                PublishState(TransportState.Error);
                _logger.Error(
                    ex,
                    "Failed to connect to WebSocket server at {Url}. proxyMode={ProxyMode} timeoutSeconds={TimeoutSeconds}",
                    url,
                    _proxyConfiguration.Mode,
                    _connectTimeout.TotalSeconds);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task DisconnectAsync()
        {
            var client = _client;
            if (client == null || !client.IsRunning)
            {
                _logger.Warning("WebSocket is not connected");
                return;
            }

            try
            {
                PublishState(TransportState.Disconnecting);
                _logger.Information("Disconnecting from WebSocket server");

                await client.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Client disconnecting");

                PublishState(TransportState.Disconnected);
                _logger.Information("Successfully disconnected from WebSocket server");
            }
            catch (Exception ex)
            {
                PublishState(TransportState.Error);
                _logger.Error(ex, "Error while disconnecting from WebSocket server");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task SendAsync(string message, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Message cannot be null or empty", nameof(message));
            }

            // One capture for the whole send: teardown can null the field at any point, and re-reading
            // it would let the guard, the send and the fatality decision disagree about which client
            // they are talking about.
            var client = _client;
            if (client == null || !client.IsRunning)
            {
                throw new InvalidOperationException("WebSocket is not connected. Call ConnectAsync first.");
            }

            try
            {
                _logger.Debug("Sending message: {Message}", message);
                client.Send(message);
                await Task.CompletedTask; // Make method async-compatible
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to send message: {Message}", message);
                // A fatal send failure (the connection is actually gone) must be reflected in the
                // transport state so downstream IsConnected projections flip and in-flight requests
                // are faulted rather than hanging until timeout. A transient failure leaves the
                // connection intact. Mirrors StreamableHttpTransport.MarkTransportErrored.
                if (IsFatalSendFailure(ex, client))
                {
                    PublishState(TransportState.Error, TransportStateChangeOrigin.SendFailure, ex);
                }
                throw;
            }
        }

        /// <summary>
        /// A send fault is fatal only when the underlying connection is gone: a WebSocketException
        /// from the closed socket, or the client no longer running. Other failures (transient
        /// library-internal contention, a still-running client) leave the connection usable.
        /// </summary>
        /// <remarks>
        /// Takes the client the send actually used. A concurrent teardown can dispose it, which makes
        /// reading its state throw; that only confirms the connection is gone, and the throw must not
        /// escape here because it would replace the send failure the caller needs to see.
        /// </remarks>
        private static bool IsFatalSendFailure(Exception ex, IWebsocketClient client)
        {
            if (ex is System.Net.WebSockets.WebSocketException)
            {
                return true;
            }

            try
            {
                return !client.IsRunning;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        /// <summary>
        /// Subscribes to WebSocket client events and forwards them to observables.
        /// </summary>
        private void SubscribeToWebSocketEvents()
        {
            var client = _client ?? throw new InvalidOperationException("WebSocket client is not initialized.");
            _clientSubscriptions?.Dispose();

            _clientSubscriptions = new CompositeDisposable(
                client.MessageReceived.Subscribe(msg =>
                {
                    if (msg.MessageType == System.Net.WebSockets.WebSocketMessageType.Text)
                    {
                        var text = msg.Text;
                        if (string.IsNullOrEmpty(text))
                        {
                            _logger.Debug("Received empty WebSocket message");
                            return;
                        }

                        _logger.Debug("Received message: {Message}", text);
                        PublishMessage(text);
                    }
                }),
                client.ReconnectionHappened.Subscribe(info =>
                {
                    _logger.Information("WebSocket reconnection happened: {Type}", info.Type);
                    PublishState(TransportState.Connected);
                }),
                client.DisconnectionHappened.Subscribe(info =>
                {
                    _logger.Warning(
                        "WebSocket disconnection happened: {Type}, {CloseStatus}, exceptionType={ExceptionType}, exceptionMessage={ExceptionMessage}",
                        info.Type,
                        info.CloseStatus,
                        info.Exception?.GetType().FullName,
                        info.Exception?.Message);

                    if (info.Type != DisconnectionType.Exit && info.Type != DisconnectionType.ByUser)
                    {
                        PublishState(TransportState.Error, TransportStateChangeOrigin.ReceiveFailure);
                    }
                    else
                    {
                        PublishState(TransportState.Disconnected);
                    }
                }));
        }

        /// <summary>
        /// Disposes the WebSocketTransport and releases all resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the WebSocketTransport and releases all resources.
        /// </summary>
        /// <param name="disposing">True if disposing managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            // 先立标志再拆流，与 StreamableHttpTransport.Dispose 一致。反过来的话，标志尚未立起
            // 而流已经完成，这中间另一个线程的发送/断开会通过守卫、发到已完成的流上。本文件的
            // 测试盯不到这个跨线程窗口（它在同一线程内 Dispose），窗口本身仍然真实存在。
            _disposed = true;

            if (disposing)
            {
                try
                {
                    // Dispose 是同步契约:直接释放客户端(Websocket.Client 的 Dispose 会关闭底层连接),
                    // 不再 fire-and-forget 优雅断开与资源释放竞态;需要 NormalClosure 优雅关闭的
                    // 调用方应先 await DisconnectAsync()。
                    DisposeClient();
                    _credentialInvoker?.Dispose();

                    // Complete the subjects
                    _messagesSubject?.OnCompleted();
                    _messagesSubject?.Dispose();

                    _stateSubject?.OnCompleted();
                    _stateSubject?.Dispose();

                    _logger.Debug("WebSocketTransport disposed");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during WebSocketTransport disposal");
                }
            }
        }

        /// <summary>
        /// 发布一次状态。流一旦拆掉就不再发——此时已无订阅者，硬发只会让
        /// <see cref="ObjectDisposedException"/> 顶替调用方真正要看的失败原因。
        /// 规则集中在这里，而不是散在每个发射点上。
        /// </summary>
        private void PublishState(
            TransportState state,
            TransportStateChangeOrigin origin = TransportStateChangeOrigin.Connection,
            Exception? exception = null)
        {
            if (_disposed)
            {
                return;
            }

            _stateSubject.OnNext(new TransportStateChange(state, origin, exception));
        }

        /// <summary>
        /// 转发一条收到的消息。与 <see cref="PublishState"/> 同理：流已拆则丢弃。
        /// </summary>
        private void PublishMessage(string message)
        {
            if (_disposed)
            {
                return;
            }

            _messagesSubject.OnNext(message);
        }

        private void DisposeClient()
        {
            _clientSubscriptions?.Dispose();
            _clientSubscriptions = null;

            _client?.Dispose();
            _client = null;
        }

        private static InvalidOperationException CreateConnectFailure(string url, DisconnectionInfo disconnection)
        {
            var message = disconnection.CloseStatus.HasValue
                ? $"WebSocket connection to {url} closed before becoming ready: {disconnection.Type} ({disconnection.CloseStatus})"
                : $"WebSocket connection to {url} closed before becoming ready: {disconnection.Type}";

            var exceptionMessage = disconnection.Exception?.Message?.Trim();
            if (!string.IsNullOrWhiteSpace(exceptionMessage)
                && !message.Contains(exceptionMessage, StringComparison.Ordinal))
            {
                message += $": {exceptionMessage}";
            }

            message = AppendEndpointDiagnosticHint(url, exceptionMessage, message);

            return new InvalidOperationException(message, disconnection.Exception);
        }

        private static string AppendEndpointDiagnosticHint(string url, string? exceptionMessage, string message)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase))
            {
                return message;
            }

            if (string.IsNullOrWhiteSpace(exceptionMessage)
                || !exceptionMessage.Contains("non-101", StringComparison.OrdinalIgnoreCase))
            {
                return message;
            }

            return message
                + " The endpoint did not complete a WebSocket 101 upgrade. "
                + "For browser/WASM clients, verify the ws:// endpoint does not redirect and that the configured path accepts WebSocket Upgrade directly; "
                + "if the server intentionally requires TLS, update the ACP profile to the matching wss:// endpoint.";
        }

        internal static ClientWebSocket CreateNativeClient(ProxyConfig? proxyConfiguration = null)
        {
            var client = new ClientWebSocket();
            var mode = proxyConfiguration?.Mode ?? ProxyConfig.DefaultMode;

            switch (mode)
            {
                case ProxyMode.None:
                    client.Options.Proxy = null;
                    break;
                case ProxyMode.System:
                    break;
                case ProxyMode.Custom:
                    if (string.IsNullOrWhiteSpace(proxyConfiguration?.ProxyUrl))
                    {
                        throw new InvalidOperationException("Custom proxy mode requires a proxy URL.");
                    }

                    client.Options.Proxy = new WebProxy(new Uri(proxyConfiguration.ProxyUrl, UriKind.Absolute));
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported proxy mode: {mode}");
            }

            return client;
        }

        internal static WebsocketClient CreateClient(Uri uri, ProxyConfig? proxyConfiguration, TimeSpan connectTimeout)
        {
            var client = new WebsocketClient(uri, () => CreateNativeClient(proxyConfiguration))
            {
                ConnectTimeout = connectTimeout
            };

            return client;
        }

        private IWebsocketClient CreateCredentialClient(Uri uri, ProxyConfig proxyConfiguration)
        {
            BoundCredentialHeader.EnsureEndpoint(_credential, uri);
            return new WebsocketClient(uri, logger: null, connectionFactory: ConnectCredentialSocketAsync)
            {
                ConnectTimeout = _connectTimeout,
            };
        }

        private async Task<WebSocket> ConnectCredentialSocketAsync(Uri uri, CancellationToken cancellationToken)
        {
            // Websocket.Client calls this for every reconnect. Recheck its destination before the
            // HTTP upgrade and supply our own invoker: ClientWebSocket otherwise follows redirects.
            BoundCredentialHeader.EnsureEndpoint(_credential, uri);
            var client = new ClientWebSocket();
            try
            {
                client.Options.SetRequestHeader(_credential!.HeaderName!, _credential.HeaderValue!);
                await client.ConnectAsync(uri, _credentialInvoker!, cancellationToken).ConfigureAwait(false);
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private static HttpMessageInvoker CreateCredentialInvoker(ProxyConfig proxy)
        {
            var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
            switch (proxy.Mode)
            {
                case ProxyMode.None:
                    handler.UseProxy = false;
                    break;
                case ProxyMode.System:
                    break;
                case ProxyMode.Custom when !string.IsNullOrWhiteSpace(proxy.ProxyUrl):
                    handler.Proxy = new WebProxy(new Uri(proxy.ProxyUrl, UriKind.Absolute));
                    break;
                default:
                    handler.Dispose();
                    throw new InvalidOperationException("Custom proxy mode requires a proxy URL.");
            }

            return new HttpMessageInvoker(handler, disposeHandler: true);
        }

        private static ProxyConfig CloneProxyConfiguration(ProxyConfig? proxyConfiguration)
            => new()
            {
                Mode = proxyConfiguration?.Mode ?? ProxyConfig.DefaultMode,
                ProxyUrl = proxyConfiguration?.ProxyUrl
            };
    }
}
