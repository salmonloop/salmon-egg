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

    [Fact]
    public async Task Disconnect_SendsDeleteWithConnectionId()
    {
        await InitializeAsync();

        await _transport.DisconnectAsync();

        await WaitForAsync(() => _server.Deletes.Count == 1);
        Assert.Equal(ConnectionId, _server.Deletes.Single());
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

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
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
        private readonly Pipe _connectionPipe = new();

        public bool OmitConnectionIdOnInitialize { get; set; }

        public ConcurrentQueue<PostRequest> Posts { get; } = new();

        public ConcurrentQueue<StreamRequest> ConnectionStreamRequests { get; } = new();

        public ConcurrentQueue<StreamRequest> SessionStreamRequests { get; } = new();

        public ConcurrentQueue<string> Deletes { get; } = new();

        public Task EmitOnConnectionStreamAsync(string message)
            => WriteSseAsync(_connectionPipe, message);

        public Task EmitOnSessionStreamAsync(string sessionId, string message)
            => WriteSseAsync(_sessionPipes.GetOrAdd(sessionId, _ => new Pipe()), message);

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

                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (body.Contains("\"initialize\"", StringComparison.Ordinal))
                {
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

                Posts.Enqueue(new PostRequest(body, connectionId, sessionId));
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            var accept = string.Join(",", request.Headers.Accept.Select(header => header.ToString()));
            var streamRequest = new StreamRequest(connectionId, sessionId, accept);
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
