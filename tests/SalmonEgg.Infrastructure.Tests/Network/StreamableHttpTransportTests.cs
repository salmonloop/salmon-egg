using System;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Infrastructure.Network;
using Serilog;

namespace SalmonEgg.Infrastructure.Tests.Network;

/// <summary>
/// 按 ACP 官方草案 RFD「Streamable HTTP &amp; WebSocket Transport」验证客户端行为:
/// initialize 200 + Acp-Connection-Id、后续 POST 202 + 头部路由、
/// 连接级/会话级 SSE 流送达、DELETE 终止。
/// </summary>
public sealed class StreamableHttpTransportTests : IDisposable
{
    private const string Endpoint = "http://localhost:9464/acp";
    private const string ConnectionId = "conn-123";

    private readonly Mock<ILogger> _logger = new(MockBehavior.Loose);
    private readonly FakeAcpServerHandler _server = new();
    private readonly StreamableHttpTransport _transport;
    private readonly ConcurrentQueue<string> _received = new();

    public StreamableHttpTransportTests()
    {
        _transport = new StreamableHttpTransport(_logger.Object, new HttpClient(_server));
        _transport.Messages.Subscribe(message => _received.Enqueue(message));
    }

    [Fact]
    public async Task Initialize_CapturesConnectionId_ForwardsBody_AndOpensConnectionStream()
    {
        await _transport.ConnectAsync(Endpoint, TestContext.Current.CancellationToken);

        await _transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}", TestContext.Current.CancellationToken);

        Assert.Contains(_received, message => message.Contains("\"protocolVersion\"", StringComparison.Ordinal));
        await WaitForAsync(() => _server.ConnectionStreamRequests.Count == 1);
        var streamRequest = _server.ConnectionStreamRequests.Single();
        Assert.Equal(ConnectionId, streamRequest.ConnectionId);
        Assert.Null(streamRequest.SessionId);
        Assert.Contains("text/event-stream", streamRequest.Accept, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_BeforeInitializeCompletes_Throws()
    {
        await _transport.ConnectAsync(Endpoint, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{}}", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Initialize_WithoutConnectionIdHeader_Throws()
    {
        _server.OmitConnectionIdOnInitialize = true;
        await _transport.ConnectAsync(Endpoint, TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}", TestContext.Current.CancellationToken));

        Assert.Contains("Acp-Connection-Id", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initialize_WhenServerNeverResponds_TimesOutWithinConnectTimeout()
    {
        // 握手 POST 是有界操作:HttpClient 配的是 InfiniteTimeSpan,若不给连接超时,
        // 服务器不应答会让 initialize 永久悬挂。连接超时须把它转成 TimeoutException。
        _server.HangInitializeRequests = true;
        using var transport = new StreamableHttpTransport(
            _logger.Object,
            new HttpClient(_server),
            connectTimeout: TimeSpan.FromMilliseconds(200));
        await transport.ConnectAsync(Endpoint, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_CustomProxyModeWithoutUrl_Throws()
    {
        // 自建 HttpClient 时按 ProxyConfig 装配 handler,与 WebSocketTransport 同一套代理事实源;
        // Custom 模式缺 URL 是配置错误,须与 WebSocket 路径一致地快速失败。
        Assert.Throws<InvalidOperationException>(() =>
            new StreamableHttpTransport(
                _logger.Object,
                httpClient: null,
                proxyConfiguration: new SalmonEgg.Domain.Models.ProxyConfig
                {
                    Mode = SalmonEgg.Domain.Models.ProxyMode.Custom,
                    ProxyUrl = null
                }));
    }

    [Fact]
    public void Constructor_CustomProxyModeWithUrl_BuildsProxiedClient()
    {
        // Custom 模式带合法 URL 时应成功装配自有 HttpClient(不注入),不抛。
        using var transport = new StreamableHttpTransport(
            _logger.Object,
            httpClient: null,
            proxyConfiguration: new SalmonEgg.Domain.Models.ProxyConfig
            {
                Mode = SalmonEgg.Domain.Models.ProxyMode.Custom,
                ProxyUrl = "http://127.0.0.1:8888"
            });

        Assert.NotNull(transport);
    }

    [Fact]
    public async Task SessionNewResponseOnConnectionStream_OpensSessionStream_AndPromptCarriesSessionHeader()
    {
        await InitializeAsync();

        await _transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{\"cwd\":\"/repo\"}}", TestContext.Current.CancellationToken);
        var sessionNewPost = _server.Posts.Single(post => post.Body.Contains("session/new", StringComparison.Ordinal));
        Assert.Equal(ConnectionId, sessionNewPost.ConnectionId);
        Assert.Null(sessionNewPost.SessionId);

        await _server.EmitOnConnectionStreamAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"sessionId\":\"sess-9\"}}");

        await WaitForAsync(() => _received.Any(message => message.Contains("sess-9", StringComparison.Ordinal)));
        await WaitForAsync(() => _server.SessionStreamRequests.Any(request => request.SessionId == "sess-9"));

        await _transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"session/prompt\",\"params\":{\"sessionId\":\"sess-9\",\"prompt\":[]}}", TestContext.Current.CancellationToken);
        var promptPost = _server.Posts.Single(post => post.Body.Contains("session/prompt", StringComparison.Ordinal));
        Assert.Equal(ConnectionId, promptPost.ConnectionId);
        Assert.Equal("sess-9", promptPost.SessionId);

        await _server.EmitOnSessionStreamAsync("sess-9", "{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{\"stopReason\":\"end_turn\"}}");
        await WaitForAsync(() => _received.Any(message => message.Contains("end_turn", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PermissionResponse_UsesSessionHeaderOfDeliveringStream()
    {
        await InitializeAsync();
        await _transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"session/new\",\"params\":{}}", TestContext.Current.CancellationToken);
        await _server.EmitOnConnectionStreamAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"sessionId\":\"sess-9\"}}");
        await WaitForAsync(() => _server.SessionStreamRequests.Any(request => request.SessionId == "sess-9"));

        await _server.EmitOnSessionStreamAsync("sess-9", "{\"jsonrpc\":\"2.0\",\"id\":77,\"method\":\"session/request_permission\",\"params\":{\"sessionId\":\"sess-9\"}}");
        await WaitForAsync(() => _received.Any(message => message.Contains("request_permission", StringComparison.Ordinal)));

        await _transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":77,\"result\":{\"outcome\":{\"outcome\":\"selected\",\"optionId\":\"allow\"}}}", TestContext.Current.CancellationToken);
        var permissionPost = _server.Posts.Single(post => post.Body.Contains("\"id\":77", StringComparison.Ordinal));
        Assert.Equal("sess-9", permissionPost.SessionId);
    }

    [Theory]
    [InlineData("77", "\"77\"")]
    [InlineData("\"77\"", "77")]
    [InlineData("null", "\"null\"")]
    public async Task PermissionResponses_WithDistinctIdTypes_KeepTheirSessionRoutes(string firstId, string secondId)
    {
        // Arrange: ids that look alike in diagnostics still name different JSON-RPC requests.
        await InitializeAsync();
        await OpenSessionAsync("first", 2);
        await OpenSessionAsync("second", 3);
        await EmitPermissionAsync("first", firstId);
        await EmitPermissionAsync("second", secondId);

        // Act
        await _transport.SendAsync(PermissionResponse(firstId), TestContext.Current.CancellationToken);
        await _transport.SendAsync(PermissionResponse(secondId), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("first", _server.Posts.Single(post => post.Body == PermissionResponse(firstId)).SessionId);
        Assert.Equal("second", _server.Posts.Single(post => post.Body == PermissionResponse(secondId)).SessionId);
    }

    [Theory]
    [InlineData("first")]
    [InlineData("second")]
    [InlineData(null)]
    public async Task PermissionResponse_WhenPeerReusesIdDuringPostCompletion_PreservesTheNewRoute(string? nextSession)
    {
        // Arrange: SSE can deliver the next request before the previous POST completion is observed.
        await InitializeAsync();
        await OpenSessionAsync("first", 2);
        await OpenSessionAsync("second", 3);
        await EmitPermissionAsync("first", "77");
        var posted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string connectionResponse = "{\"jsonrpc\":\"2.0\",\"id\":77,\"result\":{}}";
        _server.PostResponse = (post, ct) =>
        {
            if (post.Body == connectionResponse) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
            posted.TrySetResult();
            return accepted.Task.WaitAsync(ct);
        };

        // Act
        var previousResponse = _transport.SendAsync(PermissionResponse("77"), TestContext.Current.CancellationToken);
        await posted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (nextSession is not null)
        {
            await EmitPermissionAsync(nextSession, "77", "next");
        }
        else
        {
            const string connectionRequest = "{\"jsonrpc\":\"2.0\",\"id\":77,\"method\":\"_ping\",\"params\":{}}";
            await _server.EmitOnConnectionStreamAsync(connectionRequest);
            await WaitForAsync(() => _received.Contains(connectionRequest));
            await _transport.SendAsync(connectionResponse, TestContext.Current.CancellationToken);
            Assert.Null(_server.Posts.Single(post => post.Body == connectionResponse).SessionId);
        }
        accepted.SetResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        await previousResponse;
        _server.PostResponse = null;
        await _transport.SendAsync(PermissionResponse("77"), TestContext.Current.CancellationToken);

        // Assert: this also covers reuse on the same session, where comparing only the strings fails.
        var replies = _server.Posts.Where(post => post.Body == PermissionResponse("77")).ToArray();
        Assert.Equal(2, replies.Length);
        Assert.Equal("first", replies[0].SessionId);
        Assert.Equal(nextSession, replies[1].SessionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionResponse_WhenPostThrowsOrIsCancelled_RetainsRouteForRetry(bool cancel)
    {
        // Arrange
        await InitializeAsync();
        await OpenSessionAsync("first", 2);
        await EmitPermissionAsync("first", "77");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _server.PostResponse = async (_, ct) =>
        {
            if (cancel)
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            throw new HttpRequestException("Temporary write failure.");
        };

        // Act
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _transport.SendAsync(PermissionResponse("77"), cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => _transport.SendAsync(PermissionResponse("77"), cancellation.Token));
        }
        _server.PostResponse = null;
        await _transport.SendAsync(PermissionResponse("77"), TestContext.Current.CancellationToken);

        // Assert
        var replies = _server.Posts.Where(post => post.Body == PermissionResponse("77")).ToArray();
        Assert.Equal(2, replies.Length);
        Assert.All(replies, post => Assert.Equal("first", post.SessionId));
    }

    [Fact]
    public async Task PermissionResponse_AfterSuccessfulPost_DoesNotLeakRouteIntoConnectionRequest()
    {
        // Arrange
        await InitializeAsync();
        await OpenSessionAsync("first", 2);
        await EmitPermissionAsync("first", "77");
        await _transport.SendAsync(PermissionResponse("77"), TestContext.Current.CancellationToken);

        // Act: a later connection-level request legitimately reuses the now-completed id.
        const string connectionRequest = "{\"jsonrpc\":\"2.0\",\"id\":77,\"method\":\"_ping\",\"params\":{}}";
        await _server.EmitOnConnectionStreamAsync(connectionRequest);
        await WaitForAsync(() => _received.Contains(connectionRequest));
        const string response = "{\"jsonrpc\":\"2.0\",\"id\":77,\"result\":{}}";
        await _transport.SendAsync(response, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(_server.Posts.Single(post => post.Body == response).SessionId);
    }

    [Fact]
    public async Task PermissionResponse_WithInlineConnectionRequest_ReentrantReplyUsesTheNewScope()
    {
        // Arrange: the peer's successful POST body can contain its next request.
        await InitializeAsync();
        await OpenSessionAsync("first", 2);
        await EmitPermissionAsync("first", "77");
        const string connectionRequest = "{\"jsonrpc\":\"2.0\",\"id\":77,\"method\":\"_ping\",\"params\":{}}";
        const string connectionResponse = "{\"jsonrpc\":\"2.0\",\"id\":77,\"result\":{}}";
        var replied = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = _transport.Messages.Subscribe(message =>
        {
            if (message == connectionRequest)
            {
                replied.SetResult(_transport.SendAsync(connectionResponse, TestContext.Current.CancellationToken));
            }
        });
        _server.PostResponse = (post, _) => Task.FromResult(post.Body == connectionResponse
            ? new HttpResponseMessage(HttpStatusCode.Accepted)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(connectionRequest, Encoding.UTF8, "application/json")
            });

        // Act
        await _transport.SendAsync(PermissionResponse("77"), TestContext.Current.CancellationToken);
        var reply = await replied.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await reply;

        // Assert
        Assert.Null(_server.Posts.Single(post => post.Body == connectionResponse).SessionId);
    }

    [Fact]
    public async Task PermissionResponse_WhenOldPostCompletesAfterReconnect_PreservesTheNewConnectionRoute()
    {
        // Arrange: a server can finish an accepted POST after the original connection closes.
        await InitializeAsync();
        await OpenSessionAsync("first", 2);
        await EmitPermissionAsync("first", "77");
        var posted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.PostResponse = (_, _) =>
        {
            posted.SetResult();
            return accepted.Task;
        };
        var oldResponse = _transport.SendAsync(PermissionResponse("77"), TestContext.Current.CancellationToken);
        await posted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Act: the new connection receives another request with the same id and session spelling.
        await _transport.DisconnectAsync();
        _server.PostResponse = null;
        _server.RestartStreams();
        await _transport.ConnectAsync(Endpoint, TestContext.Current.CancellationToken);
        await _transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}", TestContext.Current.CancellationToken);
        await WaitForAsync(() => _server.ConnectionStreamRequests.Count == 2);
        await OpenSessionAsync("first", 3);
        await EmitPermissionAsync("first", "77", "new-connection");
        accepted.SetResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        await oldResponse;
        await _transport.SendAsync(PermissionResponse("77"), TestContext.Current.CancellationToken);

        // Assert
        var replies = _server.Posts.Where(post => post.Body == PermissionResponse("77")).ToArray();
        Assert.Equal(2, replies.Length);
        Assert.All(replies, post => Assert.Equal("first", post.SessionId));
    }

    [Fact]
    public async Task Disconnect_SendsDeleteWithConnectionId()
    {
        await InitializeAsync();

        await _transport.DisconnectAsync();

        await WaitForAsync(() => _server.Deletes.Count == 1);
        Assert.Equal(ConnectionId, _server.Deletes.Single());
    }

    [Fact]
    public async Task ConnectionStream_WhenItRepeatedlyFailsWithoutProgress_MarksTransportErrored()
    {
        var states = new ConcurrentQueue<TransportState>();
        _transport.StateChanges.Subscribe(states.Enqueue);
        _server.FailStreamRequests = true;

        await _transport.ConnectAsync(Endpoint, TestContext.Current.CancellationToken);
        await _transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}", TestContext.Current.CancellationToken);

        // 连接流对每次 GET 立即失败,达到失败预算后传输必须进入 Error,而不是无限重连。
        await WaitForAsync(() => states.Contains(TransportState.Error), timeoutSeconds: 30);
    }

    [Fact]
    public async Task Disconnect_WhenTerminateHangs_DoesNotBlockTeardown()
    {
        await InitializeAsync();
        _server.HangDeleteRequests = true;

        // DELETE 无响应时,有界的终止超时必须让断开在合理时间内返回而非永久阻塞。
        await _transport.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _transport.Dispose();
        _server.Dispose();
    }

    private async Task InitializeAsync()
    {
        await _transport.ConnectAsync(Endpoint, TestContext.Current.CancellationToken);
        await _transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}", TestContext.Current.CancellationToken);
        await WaitForAsync(() => _server.ConnectionStreamRequests.Count == 1);
    }

    private async Task OpenSessionAsync(string sessionId, int requestId)
    {
        await _transport.SendAsync($$$$"""{"jsonrpc":"2.0","id":{{{{requestId}}}},"method":"session/new","params":{}}""", TestContext.Current.CancellationToken);
        await _server.EmitOnConnectionStreamAsync($$$$"""{"jsonrpc":"2.0","id":{{{{requestId}}}},"result":{"sessionId":"{{{{sessionId}}}}"}}""");
        await WaitForAsync(() => _server.SessionStreamRequests.Any(request => request.SessionId == sessionId));
    }

    private async Task EmitPermissionAsync(string sessionId, string id, string toolCallId = "tool")
    {
        var message = $$$$"""{"jsonrpc":"2.0","id":{{{{id}}}},"method":"session/request_permission","params":{"sessionId":"{{{{sessionId}}}}","toolCall":{"toolCallId":"{{{{toolCallId}}}}"}}}""";
        await _server.EmitOnSessionStreamAsync(sessionId, message);
        await WaitForAsync(() => _received.Contains(message));
    }

    private static string PermissionResponse(string id)
        => $$$$"""{"jsonrpc":"2.0","id":{{{{id}}}},"result":{"outcome":{"outcome":"cancelled"}}}""";

    private static async Task WaitForAsync(Func<bool> condition, int timeoutSeconds = 5)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Condition was not reached within the allotted time.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed record StreamRequest(string? ConnectionId, string? SessionId, string Accept);

    private sealed record PostRequest(string Body, string? ConnectionId, string? SessionId);

    /// <summary>
    /// 按草案路由的假服务器:POST initialize → 200 + Acp-Connection-Id;
    /// 其余 POST → 202;GET(Accept: text/event-stream)→ pipe 背压的 SSE 流;
    /// DELETE → 202。测试通过 Emit* 向对应流推送事件。
    /// </summary>
    private sealed class FakeAcpServerHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, Pipe> _sessionPipes = new();
        private Pipe _connectionPipe = new();

        public bool OmitConnectionIdOnInitialize { get; set; }

        public bool FailStreamRequests { get; set; }

        public bool HangDeleteRequests { get; set; }

        public bool HangInitializeRequests { get; set; }

        public Func<PostRequest, CancellationToken, Task<HttpResponseMessage>>? PostResponse { get; set; }

        public ConcurrentQueue<PostRequest> Posts { get; } = new();

        public ConcurrentQueue<StreamRequest> ConnectionStreamRequests { get; } = new();

        public ConcurrentQueue<StreamRequest> SessionStreamRequests { get; } = new();

        public ConcurrentQueue<string> Deletes { get; } = new();

        public Task EmitOnConnectionStreamAsync(string message)
            => WriteSseAsync(_connectionPipe, message);

        public Task EmitOnSessionStreamAsync(string sessionId, string message)
            => WriteSseAsync(_sessionPipes.GetOrAdd(sessionId, _ => new Pipe()), message);

        public void RestartStreams()
        {
            _connectionPipe = new Pipe();
            _sessionPipes.Clear();
            SessionStreamRequests.Clear();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var connectionId = ReadHeader(request, "Acp-Connection-Id");
            var sessionId = ReadHeader(request, "Acp-Session-Id");

            if (request.Method == HttpMethod.Delete)
            {
                if (connectionId is not null)
                {
                    Deletes.Enqueue(connectionId);
                }

                if (HangDeleteRequests)
                {
                    // 模拟服务器对 DELETE 永不响应:若终止未加有界超时,teardown 会永久阻塞。
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (body.Contains("\"initialize\"", StringComparison.Ordinal))
                {
                    if (HangInitializeRequests)
                    {
                        // 模拟服务器对 initialize 握手永不应答:若握手未加连接超时,SendAsync 会永久悬挂。
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }

                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":1,\"connectionId\":\"" + ConnectionId + "\"}}",
                            Encoding.UTF8,
                            "application/json")
                    };
                    if (!OmitConnectionIdOnInitialize)
                    {
                        response.Headers.Add("Acp-Connection-Id", ConnectionId);
                    }

                    return response;
                }

                var post = new PostRequest(body, connectionId, sessionId);
                Posts.Enqueue(post);
                return PostResponse is { } respond
                    ? await respond(post, cancellationToken)
                    : new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            var accept = string.Join(",", request.Headers.Accept.Select(header => header.ToString()));
            var streamRequest = new StreamRequest(connectionId, sessionId, accept);

            if (FailStreamRequests)
            {
                // 模拟 SSE 流持续无法建立(服务端 5xx):驱动传输的有界重试预算耗尽路径。
                if (sessionId is null)
                {
                    ConnectionStreamRequests.Enqueue(streamRequest);
                }
                else
                {
                    SessionStreamRequests.Enqueue(streamRequest);
                }

                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            Pipe pipe;
            if (sessionId is null)
            {
                ConnectionStreamRequests.Enqueue(streamRequest);
                pipe = _connectionPipe;
            }
            else
            {
                SessionStreamRequests.Enqueue(streamRequest);
                pipe = _sessionPipes.GetOrAdd(sessionId, _ => new Pipe());
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(pipe.Reader.AsStream())
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream") }
                }
            };
        }

        private static async Task WriteSseAsync(Pipe pipe, string message)
        {
            var payload = Encoding.UTF8.GetBytes("data: " + message + "\n\n");
            await pipe.Writer.WriteAsync(payload);
            await pipe.Writer.FlushAsync();
        }

        private static string? ReadHeader(HttpRequestMessage request, string name)
            => request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}
