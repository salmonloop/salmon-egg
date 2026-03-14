using System;
using System.IO;
using System.Net.Http;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace SalmonEgg.Infrastructure.Network
{
    /// <summary>
    /// HTTP Server-Sent Events (SSE) transport implementation.
    /// Provides one-way server-to-client streaming with separate HTTP POST for client-to-server messages.
    /// </summary>
    public class HttpSseTransport : ITransport, IDisposable
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly HttpTransportOptions? _options;
        private readonly Subject<string> _messagesSubject;
        private readonly BehaviorSubject<TransportState> _stateSubject;
        private CancellationTokenSource? _connectionCts;
        private Task? _receiveTask;
        private string? _serverUrl;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the HttpSseTransport class.
        /// </summary>
        /// <param name="logger">Logger instance for logging transport events.</param>
        /// <param name="httpClient">Optional HttpClient instance. If not provided, a new one will be created.</param>
        public HttpSseTransport(ILogger logger, HttpTransportOptions? options = null, HttpClient? httpClient = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options;
            _httpClient = httpClient ?? CreateHttpClient(options);
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

            if (_connectionCts != null && !_connectionCts.IsCancellationRequested)
            {
                _logger.Warning("HTTP SSE is already connected to {Url}", url);
                return;
            }

            try
            {
                _stateSubject.OnNext(TransportState.Connecting);
                _logger.Information("Connecting to HTTP SSE server at {Url}", url);

                _serverUrl = url;
                _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                // Start receiving SSE messages
                _receiveTask = Task.Run(() => ReceiveMessagesAsync(_connectionCts.Token), _connectionCts.Token);

                // Wait a moment to ensure connection is established
                await Task.Delay(500, ct);

                _stateSubject.OnNext(TransportState.Connected);
                _logger.Information("Successfully connected to HTTP SSE server at {Url}", url);
            }
            catch (Exception ex)
            {
                _stateSubject.OnNext(TransportState.Error);
                _logger.Error(ex, "Failed to connect to HTTP SSE server at {Url}", url);
                
                // Clean up on failure
                _connectionCts?.Cancel();
                _connectionCts?.Dispose();
                _connectionCts = null;
                
                throw;
            }
        }

        /// <inheritdoc />
        public async Task DisconnectAsync()
        {
            if (_connectionCts == null || _connectionCts.IsCancellationRequested)
            {
                _logger.Warning("HTTP SSE is not connected");
                return;
            }

            try
            {
                _stateSubject.OnNext(TransportState.Disconnecting);
                _logger.Information("Disconnecting from HTTP SSE server");

                // Cancel the connection
                _connectionCts.Cancel();

                // Wait for receive task to complete
                if (_receiveTask != null)
                {
                    try
                    {
                        await _receiveTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancelling
                    }
                }

                _stateSubject.OnNext(TransportState.Disconnected);
                _logger.Information("Successfully disconnected from HTTP SSE server");
            }
            catch (Exception ex)
            {
                _stateSubject.OnNext(TransportState.Error);
                _logger.Error(ex, "Error while disconnecting from HTTP SSE server");
                throw;
            }
            finally
            {
                _connectionCts?.Dispose();
                _connectionCts = null;
                _receiveTask = null;
            }
        }

        /// <inheritdoc />
        public async Task SendAsync(string message, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Message cannot be null or empty", nameof(message));
            }

            if (_connectionCts == null || _connectionCts.IsCancellationRequested)
            {
                throw new InvalidOperationException("HTTP SSE is not connected. Call ConnectAsync first.");
            }

            try
            {
                _logger.Debug("Sending message via HTTP POST: {Message}", message);

                // Send message via HTTP POST
                var content = new StringContent(message, Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, _serverUrl)
                {
                    Content = content
                };
                ApplyRequestHeaders(request);
                var response = await _httpClient.SendAsync(request, ct);

                response.EnsureSuccessStatusCode();

                _logger.Debug("Message sent successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to send message: {Message}", message);
                throw;
            }
        }

        /// <summary>
        /// Receives messages from the SSE stream.
        /// </summary>
        /// <param name="ct">Cancellation token to stop receiving messages.</param>
        private async Task ReceiveMessagesAsync(CancellationToken ct)
        {
            try
            {
                _logger.Debug("Starting SSE message receive loop");

                // Create request with SSE headers
                var request = new HttpRequestMessage(HttpMethod.Get, _serverUrl);
                request.Headers.Add("Accept", "text/event-stream");
                request.Headers.Add("Cache-Control", "no-cache");
                ApplyRequestHeaders(request);

                // Send request and get response stream
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                // Read the stream
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var dataBuilder = new StringBuilder();

                    while (!ct.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();

                        // Check for end of stream
                        if (line == null)
                        {
                            _logger.Information("SSE stream ended");
                            break;
                        }

                        // SSE format: "data: {message}\n\n"
                        if (line.StartsWith("data: "))
                        {
                            // Extract the data content
                            var data = line.Substring(6); // Remove "data: " prefix
                            dataBuilder.Append(data);
                        }
                        else if (string.IsNullOrEmpty(line))
                        {
                            // Empty line indicates end of message
                            if (dataBuilder.Length > 0)
                            {
                                var message = dataBuilder.ToString();
                                _logger.Debug("Received SSE message: {Message}", message);
                                _messagesSubject.OnNext(message);
                                dataBuilder.Clear();
                            }
                        }
                        else if (line.StartsWith(":"))
                        {
                            // Comment line, ignore
                            _logger.Debug("Received SSE comment: {Line}", line);
                        }
                        else if (line.Contains(":"))
                        {
                            // Other SSE fields (event, id, retry) - log but don't process
                            _logger.Debug("Received SSE field: {Line}", line);
                        }
                    }
                }

                _logger.Debug("SSE message receive loop ended");
            }
            catch (OperationCanceledException)
            {
                _logger.Information("SSE receive loop cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in SSE receive loop");
                _stateSubject.OnNext(TransportState.Error);
            }
        }

        private static HttpClient CreateHttpClient(HttpTransportOptions? options)
        {
            var handler = new HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(options?.ProxyUrl))
            {
                try
                {
                    handler.Proxy = new System.Net.WebProxy(new Uri(options.ProxyUrl));
                    handler.UseProxy = true;
                }
                catch
                {
                    // Ignore invalid proxy; transport will still try direct.
                }
            }

            return new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        private void ApplyRequestHeaders(HttpRequestMessage request)
        {
            if (_options == null)
            {
                return;
            }

            foreach (var kvp in _options.Headers)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    continue;
                }
                request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value ?? string.Empty);
            }
        }

        /// <summary>
        /// Disposes the HttpSseTransport and releases all resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the HttpSseTransport and releases all resources.
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
                    if (_connectionCts != null && !_connectionCts.IsCancellationRequested)
                    {
                        DisconnectAsync().GetAwaiter().GetResult();
                    }

                    // Dispose resources
                    _connectionCts?.Dispose();
                    _httpClient?.Dispose();

                    // Complete the subjects
                    _messagesSubject?.OnCompleted();
                    _messagesSubject?.Dispose();

                    _stateSubject?.OnCompleted();
                    _stateSubject?.Dispose();

                    _logger.Debug("HttpSseTransport disposed");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during HttpSseTransport disposal");
                }
            }

            _disposed = true;
        }
    }
}
