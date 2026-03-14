using System;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Websocket.Client;

namespace SalmonEgg.Infrastructure.Network
{
    /// <summary>
    /// WebSocket transport implementation using Websocket.Client library.
    /// Provides message streaming using Reactive Extensions.
    /// </summary>
    public class WebSocketTransport : ITransport, IDisposable
    {
        private readonly ILogger _logger;
        private IWebsocketClient? _client;
        private readonly HttpTransportOptions? _options;
        private readonly Subject<string> _messagesSubject;
        private readonly BehaviorSubject<TransportState> _stateSubject;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the WebSocketTransport class.
        /// </summary>
        /// <param name="logger">Logger instance for logging transport events.</param>
        public WebSocketTransport(ILogger logger, HttpTransportOptions? options = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options;
            _messagesSubject = new Subject<string>();
            _stateSubject = new BehaviorSubject<TransportState>(TransportState.Disconnected);
        }

        /// <inheritdoc />
        public IObservable<string> Messages => _messagesSubject;

        /// <inheritdoc />
        public IObservable<TransportState> StateChanges => _stateSubject;

        /// <inheritdoc />
        public async Task ConnectAsync(string url, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            }

            if (_client != null && _client.IsRunning)
            {
                _logger.Warning("WebSocket is already connected to {Url}", url);
                return;
            }

            try
            {
                _stateSubject.OnNext(TransportState.Connecting);
                _logger.Information("Connecting to WebSocket server at {Url}", url);

                // Create WebSocket client
                var uri = new Uri(url);
                _client = new WebsocketClient(uri, () =>
                {
                    var ws = new System.Net.WebSockets.ClientWebSocket();
                    ApplyOptions(ws);
                    return ws;
                });

                // Configure reconnection strategy (disabled for manual control)
                _client.ReconnectTimeout = null;

                // Subscribe to WebSocket events
                SubscribeToWebSocketEvents();

                // Start the WebSocket connection
                await _client.Start();

                // Wait for connection to be established or timeout
                var connectionTimeout = TimeSpan.FromSeconds(10);
                var startTime = DateTime.UtcNow;

                while (!_client.IsRunning && DateTime.UtcNow - startTime < connectionTimeout)
                {
                    if (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("Connection cancelled by user", ct);
                    }

                    await Task.Delay(100, ct);
                }

                if (!_client.IsRunning)
                {
                    throw new TimeoutException($"Failed to connect to {url} within {connectionTimeout.TotalSeconds} seconds");
                }

                _stateSubject.OnNext(TransportState.Connected);
                _logger.Information("Successfully connected to WebSocket server at {Url}", url);
            }
            catch (Exception ex)
            {
                _stateSubject.OnNext(TransportState.Error);
                _logger.Error(ex, "Failed to connect to WebSocket server at {Url}", url);
                throw;
            }
        }

        private void ApplyOptions(System.Net.WebSockets.ClientWebSocket ws)
        {
            if (_options == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_options.ProxyUrl))
            {
                try
                {
                    ws.Options.Proxy = new System.Net.WebProxy(new Uri(_options.ProxyUrl));
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to apply WebSocket proxy");
                }
            }

            foreach (var kvp in _options.Headers)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    continue;
                }
                ws.Options.SetRequestHeader(kvp.Key, kvp.Value ?? string.Empty);
            }
        }

        /// <inheritdoc />
        public async Task DisconnectAsync()
        {
            if (_client == null || !_client.IsRunning)
            {
                _logger.Warning("WebSocket is not connected");
                return;
            }

            try
            {
                _stateSubject.OnNext(TransportState.Disconnecting);
                _logger.Information("Disconnecting from WebSocket server");

                await _client.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Client disconnecting");

                _stateSubject.OnNext(TransportState.Disconnected);
                _logger.Information("Successfully disconnected from WebSocket server");
            }
            catch (Exception ex)
            {
                _stateSubject.OnNext(TransportState.Error);
                _logger.Error(ex, "Error while disconnecting from WebSocket server");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task SendAsync(string message, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Message cannot be null or empty", nameof(message));
            }

            if (_client == null || !_client.IsRunning)
            {
                throw new InvalidOperationException("WebSocket is not connected. Call ConnectAsync first.");
            }

            try
            {
                _logger.Debug("Sending message: {Message}", message);
                _client.Send(message);
                await Task.CompletedTask; // Make method async-compatible
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to send message: {Message}", message);
                throw;
            }
        }

        /// <summary>
        /// Subscribes to WebSocket client events and forwards them to observables.
        /// </summary>
        private void SubscribeToWebSocketEvents()
        {
            var client = _client ?? throw new InvalidOperationException("WebSocket client is not initialized.");

            // Handle incoming messages
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
                    _messagesSubject.OnNext(text);
                }
            });

            // Handle reconnection events
            client.ReconnectionHappened.Subscribe(info =>
            {
                _logger.Information("WebSocket reconnection happened: {Type}", info.Type);
                _stateSubject.OnNext(TransportState.Connected);
            });

            // Handle disconnection events
            client.DisconnectionHappened.Subscribe(info =>
            {
                _logger.Warning("WebSocket disconnection happened: {Type}, {CloseStatus}",
                    info.Type, info.CloseStatus);

                if (info.Type != DisconnectionType.Exit)
                {
                    _stateSubject.OnNext(TransportState.Error);
                }
                else
                {
                    _stateSubject.OnNext(TransportState.Disconnected);
                }
            });
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

            if (disposing)
            {
                try
                {
                    // Disconnect if still connected
                    if (_client != null && _client.IsRunning)
                    {
                        DisconnectAsync().GetAwaiter().GetResult();
                    }

                    // Dispose the WebSocket client
                    _client?.Dispose();

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

            _disposed = true;
        }
    }
}
