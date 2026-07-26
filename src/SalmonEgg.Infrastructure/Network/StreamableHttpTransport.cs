using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace SalmonEgg.Infrastructure.Network
{
    /// <summary>
    /// ACP Streamable HTTP 传输(客户端侧),对齐官方草案 RFD
    /// "Streamable HTTP &amp; WebSocket Transport"(v1/v2 正式标准仅 stdio,本实现按草案演进):
    /// 单一端点承载全部流量;initialize POST 返回 200 + Acp-Connection-Id;
    /// 其余 POST 返回 202,真实响应经连接级/会话级 SSE GET 流按 JSON-RPC id 关联送达;
    /// DELETE 终止连接。cookie 由 HttpClient 默认容器/浏览器承载以支持粘性路由。
    /// 草案未明确处按注释中的解释实现,升级为正式标准时须复核。
    /// </summary>
    public sealed class StreamableHttpTransport : ITransport, IDisposable
    {
        private const string ConnectionIdHeader = "Acp-Connection-Id";
        private const string SessionIdHeader = "Acp-Session-Id";
        private static readonly TimeSpan StreamReconnectDelay = TimeSpan.FromSeconds(1);
        // 断流重连的有限预算:草案无流恢复语义,持续重试只会掩盖已死的连接。
        // 预算耗尽即把传输标记为 Error,让上层看门狗 fault 挂起请求而不是无限悬挂。
        private const int MaxConsecutiveStreamFailures = 5;
        // DELETE 终止与 InfiniteTimeSpan 的 HttpClient 组合会在服务器无响应时永久阻塞
        // teardown;给终止一个有界超时,超时后交由服务器自行回收连接。
        private static readonly TimeSpan TerminateTimeout = TimeSpan.FromSeconds(5);

        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly Subject<string> _messagesSubject = new();
        private readonly BehaviorSubject<TransportState> _stateSubject = new(TransportState.Disconnected);
        // 连接级流、各会话级流与 POST 内联正文是并发生产者;下游(ChatService 顺序管道)
        // 依赖 ITransport.Messages 的到达序单线程契约。所有出站发布必须经此闸串行化,
        // 保证严格的先来先发布,消除跨流的 OnNext 竞态。
        private readonly object _deliveryGate = new();

        // 出站请求 id → (method, params.sessionId):用于在响应回流时识别
        // session/new(会话 id 在 result)与 session/load(会话 id 在请求参数)以开启会话级流。
        private readonly ConcurrentDictionary<string, PendingRequest> _pendingRequests = new();

        // 会话级流上收到的服务端请求 id → sessionId:client 回发的 JSON-RPC 响应
        // (如权限响应)按草案须带 Acp-Session-Id,而响应体本身不携带 sessionId。
        private readonly ConcurrentDictionary<string, string> _inboundRequestSessions = new();

        private readonly ConcurrentDictionary<string, Lazy<Task>> _sessionStreams = new();

        private Uri? _endpoint;
        private CancellationTokenSource? _connectionCts;
        private Task? _connectionStreamTask;
        private string? _connectionId;
        private bool _disposed;

        public StreamableHttpTransport(ILogger logger, HttpClient? httpClient = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ownsHttpClient = httpClient is null;
            // SSE 流是长连接,超时交由调用方 CancellationToken 与上层协议看门狗控制。
            _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        public IObservable<string> Messages => _messagesSubject;

        public IObservable<TransportState> StateChanges => _stateSubject;

        public Task ConnectAsync(string url, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint))
            {
                throw new ArgumentException($"Invalid Streamable HTTP endpoint URL: {url}", nameof(url));
            }

            if (_connectionCts is { IsCancellationRequested: false })
            {
                _logger.Warning("Streamable HTTP transport is already connected to {Url}", url);
                return Task.CompletedTask;
            }

            _stateSubject.OnNext(TransportState.Connecting);
            _endpoint = endpoint;
            _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // 草案的连接握手由 initialize POST 完成(那时才有 Acp-Connection-Id 可开流),
            // 此处仅就绪传输;上层 ACP 客户端的第一条消息必然是 initialize。
            _stateSubject.OnNext(TransportState.Connected);
            _logger.Information("Streamable HTTP transport ready. Endpoint={Endpoint}", endpoint);
            return Task.CompletedTask;
        }

        public async Task SendAsync(string message, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Message cannot be null or empty", nameof(message));
            }

            var cts = _connectionCts;
            if (_endpoint is null || cts is null || cts.IsCancellationRequested)
            {
                throw new InvalidOperationException("Streamable HTTP transport is not connected. Call ConnectAsync first.");
            }

            var peek = MessagePeek.From(message);
            if (string.Equals(peek.Method, "initialize", StringComparison.Ordinal))
            {
                await SendInitializeAsync(message, ct).ConfigureAwait(false);
                return;
            }

            await SendSubsequentAsync(message, peek, ct).ConfigureAwait(false);
        }

        public async Task DisconnectAsync()
        {
            var cts = _connectionCts;
            if (cts is null || cts.IsCancellationRequested)
            {
                _logger.Warning("Streamable HTTP transport is not connected");
                return;
            }

            _stateSubject.OnNext(TransportState.Disconnecting);
            cts.Cancel();

            await AwaitStreamsBestEffortAsync().ConfigureAwait(false);
            await SendTerminateBestEffortAsync().ConfigureAwait(false);

            _connectionId = null;
            _pendingRequests.Clear();
            _inboundRequestSessions.Clear();
            _sessionStreams.Clear();
            _connectionStreamTask = null;
            cts.Dispose();
            _connectionCts = null;

            _stateSubject.OnNext(TransportState.Disconnected);
            _logger.Information("Streamable HTTP transport disconnected");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _connectionCts?.Cancel();
                _connectionCts?.Dispose();
                _connectionCts = null;

                if (_ownsHttpClient)
                {
                    _httpClient.Dispose();
                }

                _messagesSubject.OnCompleted();
                _messagesSubject.Dispose();
                _stateSubject.OnCompleted();
                _stateSubject.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during StreamableHttpTransport disposal");
            }

            GC.SuppressFinalize(this);
        }

        private async Task SendInitializeAsync(string message, CancellationToken ct)
        {
            using var request = CreateJsonPost(message, includeConnectionId: false, sessionId: null);
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string? connectionId = null;
            if (response.Headers.TryGetValues(ConnectionIdHeader, out var values))
            {
                foreach (var value in values)
                {
                    connectionId = value;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(connectionId))
            {
                throw new InvalidOperationException(
                    $"Streamable HTTP server did not return the required {ConnectionIdHeader} header on initialize.");
            }

            _connectionId = connectionId;
            _logger.Information("Streamable HTTP connection established. ConnectionId={ConnectionId}", connectionId);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
            {
                HandleInboundMessage(body, streamSessionId: null);
            }

            StartConnectionStream();
        }

        private async Task SendSubsequentAsync(string message, MessagePeek peek, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_connectionId))
            {
                throw new InvalidOperationException(
                    "Streamable HTTP transport has no connection id yet; initialize must complete first.");
            }

            var sessionId = ResolveOutboundSessionId(peek);
            if (peek.Id is not null && !peek.IsResponse)
            {
                _pendingRequests[peek.Id] = new PendingRequest(peek.Method, peek.ParamsSessionId);
            }

            using var request = CreateJsonPost(message, includeConnectionId: true, sessionId);
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // 草案:除 initialize 外一律 202,真实响应走 SSE 流。对 200 附带 JSON 正文的
            // 服务器保持宽松接收,把正文当作已送达的消息转发,不视为协议错误。
            if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    HandleInboundMessage(body, streamSessionId: null);
                }
            }
        }

        private string? ResolveOutboundSessionId(MessagePeek peek)
        {
            if (peek.IsResponse)
            {
                return peek.Id is not null && _inboundRequestSessions.TryRemove(peek.Id, out var sessionId)
                    ? sessionId
                    : null;
            }

            // 草案明确列出的会话级 POST 是 session/prompt、session/cancel 与权限响应;
            // 其余带 sessionId 的方法(set_mode/close 等)的响应被声明在连接级流,
            // 故按连接级发送。草案定稿若扩大会话级清单,此判定须同步。
            return peek.Method is "session/prompt" or "session/cancel"
                ? peek.ParamsSessionId
                : null;
        }

        private HttpRequestMessage CreateJsonPost(string message, bool includeConnectionId, string? sessionId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(message, Encoding.UTF8, "application/json"),
                Version = System.Net.HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
            };

            if (includeConnectionId && _connectionId is not null)
            {
                request.Headers.Add(ConnectionIdHeader, _connectionId);
            }

            if (sessionId is not null)
            {
                request.Headers.Add(SessionIdHeader, sessionId);
            }

            return request;
        }

        private void StartConnectionStream()
        {
            var cts = _connectionCts;
            if (cts is null)
            {
                return;
            }

            _connectionStreamTask ??= Task.Run(() => ReceiveStreamLoopAsync(sessionId: null, cts.Token), cts.Token);
        }

        private void EnsureSessionStream(string sessionId)
        {
            var cts = _connectionCts;
            if (cts is null || cts.IsCancellationRequested)
            {
                return;
            }

            // GetOrAdd 的值工厂在并发竞争同一 key 时可能对败者也执行一次;若在工厂内直接
            // Task.Run,败者启动的重复 SSE 流会泄漏并造成双重投递。用 Lazy<Task> 把 Task.Run
            // 推迟到 .Value 首次读取,只有字典真正采纳的那一个条目才会开流。
            var token = cts.Token;
            var lazyStream = _sessionStreams.GetOrAdd(
                sessionId,
                id => new Lazy<Task>(() => Task.Run(() => ReceiveStreamLoopAsync(id, token), token)));
            _ = lazyStream.Value;
        }

        private async Task ReceiveStreamLoopAsync(string? sessionId, CancellationToken ct)
        {
            // 草案 v1 无流恢复语义,断流期间的消息不会重放;重连是实现方职责。
            // 有界重试:连续失败(fault 或未送达任何消息的空流)达到预算即放弃并把传输标记为
            // Error,让上层看门狗 fault 挂起请求,而不是无限重连掩盖已死的连接。任何一次成功
            // 送达都重置预算——健康流被服务器正常关闭后应重新开流,而非计入失败。
            var scope = sessionId is null ? "connection" : "session";
            var consecutiveFailures = 0;
            while (!ct.IsCancellationRequested)
            {
                var madeProgress = false;
                try
                {
                    madeProgress = await ReceiveStreamOnceAsync(sessionId, ct).ConfigureAwait(false);
                    _logger.Information("Streamable HTTP SSE stream ended. Scope={Scope}", scope);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Streamable HTTP SSE stream faulted. Scope={Scope}", scope);
                }

                if (madeProgress)
                {
                    consecutiveFailures = 0;
                }
                else if (++consecutiveFailures >= MaxConsecutiveStreamFailures)
                {
                    _logger.Error(
                        "Streamable HTTP SSE stream failed {FailureCount} times without progress; marking transport as errored. Scope={Scope}",
                        consecutiveFailures,
                        scope);
                    MarkTransportErrored();
                    return;
                }

                try
                {
                    await Task.Delay(StreamReconnectDelay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private void MarkTransportErrored()
        {
            if (_disposed)
            {
                return;
            }

            // 标记 Error 后取消其余流:连接已判定为死,继续重连只会拖延看门狗对挂起请求的 fault。
            _stateSubject.OnNext(TransportState.Error);
            _connectionCts?.Cancel();
        }

        private async Task<bool> ReceiveStreamOnceAsync(string? sessionId, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint)
            {
                Version = System.Net.HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
            };
            request.Headers.Add("Accept", "text/event-stream");
            if (_connectionId is not null)
            {
                request.Headers.Add(ConnectionIdHeader, _connectionId);
            }

            if (sessionId is not null)
            {
                request.Headers.Add(SessionIdHeader, sessionId);
            }

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var accumulator = new SseEventAccumulator();
            var deliveredAny = false;
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    return deliveredAny;
                }

                if (accumulator.TryAppendLine(line, out var data) && !string.IsNullOrWhiteSpace(data))
                {
                    HandleInboundMessage(data!, sessionId);
                    deliveredAny = true;
                }
            }

            return deliveredAny;
        }

        private void HandleInboundMessage(string message, string? streamSessionId)
        {
            var peek = MessagePeek.From(message);
            if (peek.IsResponse && peek.Id is not null && _pendingRequests.TryRemove(peek.Id, out var pending))
            {
                // session/new 的会话 id 在 result;session/load 的会话 id 来自请求参数。
                // 两者都要求随即开启会话级 SSE 流,否则该会话的更新与 prompt 响应永远收不到。
                var establishedSessionId = pending.Method switch
                {
                    "session/new" => peek.ResultSessionId,
                    "session/load" => pending.SessionId,
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(establishedSessionId))
                {
                    EnsureSessionStream(establishedSessionId!);
                }
            }
            else if (!peek.IsResponse && peek.Method is not null && peek.Id is not null && streamSessionId is not null)
            {
                _inboundRequestSessions[peek.Id] = streamSessionId;
            }

            // 多个 SSE 流与 POST 内联正文并发到达;串行化发布以维持下游依赖的到达序单线程契约。
            // OnNext 只是同步转发给适配器,不阻塞,持锁窗口极短。
            lock (_deliveryGate)
            {
                _messagesSubject.OnNext(message);
            }
        }

        private async Task AwaitStreamsBestEffortAsync()
        {
            var connectionStream = _connectionStreamTask;
            if (connectionStream is not null)
            {
                try
                {
                    await connectionStream.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            foreach (var stream in _sessionStreams.Values)
            {
                if (!stream.IsValueCreated)
                {
                    continue;
                }

                try
                {
                    await stream.Value.ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        private async Task SendTerminateBestEffortAsync()
        {
            if (_endpoint is null || string.IsNullOrWhiteSpace(_connectionId))
            {
                return;
            }

            // 终止是尽力而为:HttpClient 配的是 InfiniteTimeSpan,若不给有界取消,
            // 服务器无响应时 teardown 会永久阻塞。超时后放弃,交由服务器自行回收连接。
            using var terminateCts = new CancellationTokenSource(TerminateTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, _endpoint)
                {
                    Version = System.Net.HttpVersion.Version20,
                    VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
                };
                request.Headers.Add(ConnectionIdHeader, _connectionId);
                using var response = await _httpClient.SendAsync(request, terminateCts.Token).ConfigureAwait(false);
                _logger.Information(
                    "Streamable HTTP connection terminate returned {StatusCode}",
                    (int)response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Streamable HTTP connection terminate failed; server will reap the connection.");
            }
        }

        private readonly record struct PendingRequest(string? Method, string? SessionId);

        /// <summary>
        /// 只读窥探出/入站 JSON-RPC 消息的路由要素;负载本身原样透传,绝不改写。
        /// 非法 JSON 不在传输层拒绝(由协议层裁决),返回空要素按连接级处理。
        /// </summary>
        private readonly struct MessagePeek
        {
            public string? Method { get; init; }

            public string? Id { get; init; }

            public string? ParamsSessionId { get; init; }

            public string? ResultSessionId { get; init; }

            public bool IsResponse { get; init; }

            public static MessagePeek From(string json)
            {
                try
                {
                    using var document = JsonDocument.Parse(json);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return default;
                    }

                    string? method = null;
                    if (root.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String)
                    {
                        method = methodElement.GetString();
                    }

                    string? id = null;
                    if (root.TryGetProperty("id", out var idElement)
                        && idElement.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    {
                        id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : idElement.GetRawText();
                    }

                    string? paramsSessionId = null;
                    if (root.TryGetProperty("params", out var paramsElement)
                        && paramsElement.ValueKind == JsonValueKind.Object
                        && paramsElement.TryGetProperty("sessionId", out var paramsSession)
                        && paramsSession.ValueKind == JsonValueKind.String)
                    {
                        paramsSessionId = paramsSession.GetString();
                    }

                    var isResponse = method is null
                        && (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _));
                    string? resultSessionId = null;
                    if (isResponse
                        && root.TryGetProperty("result", out var resultElement)
                        && resultElement.ValueKind == JsonValueKind.Object
                        && resultElement.TryGetProperty("sessionId", out var resultSession)
                        && resultSession.ValueKind == JsonValueKind.String)
                    {
                        resultSessionId = resultSession.GetString();
                    }

                    return new MessagePeek
                    {
                        Method = method,
                        Id = id,
                        ParamsSessionId = paramsSessionId,
                        ResultSessionId = resultSessionId,
                        IsResponse = isResponse
                    };
                }
                catch (JsonException)
                {
                    return default;
                }
            }
        }
    }
}
