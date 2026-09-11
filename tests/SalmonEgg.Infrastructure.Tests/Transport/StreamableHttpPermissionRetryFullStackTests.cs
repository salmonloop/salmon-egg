using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Network;
using Serilog;

namespace SalmonEgg.Infrastructure.Tests.Transport;

public sealed class StreamableHttpPermissionRetryFullStackTests
{
    [Theory]
    [InlineData("selected", "allow")]
    [InlineData("cancelled", null)]
    public async Task PermissionResponse_AfterTemporaryHttpFailure_RetriesOnTheOriginalSession(string outcome, string? optionId)
    {
        // Arrange: use real HTTP/2 POSTs and a session SSE stream through both production adapters.
        await using var peer = await PermissionHttpPeer.StartAsync();
        using var transport = new StreamableHttpTransport(Log.Logger);
        using var network = new NetworkTransportAdapter(transport, peer.Url);
        using var client = new AcpClient(new DomainAcpTransportAdapter(network));
        var received = new TaskCompletionSource<PermissionRequestEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PermissionRequestReceived += (_, request) => received.TrySetResult(request);
        var token = TestContext.Current.CancellationToken;
        await client.InitializeAsync(new InitializeParams(new ClientInfo("permission-retry", "1"), new ClientCapabilities()), token);
        await client.CreateSessionAsync(new SessionNewParams(Path.GetFullPath(Path.GetTempPath()), null), token);
        await peer.SessionReady.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        await peer.SendPermissionAsync(token);
        var request = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), token);

        // Act: the server rejects the first attempt without ending the connection.
        Assert.False(await request.TryRespondAsync(outcome, optionId).WaitAsync(TimeSpan.FromSeconds(5), token));
        Assert.True(request.CanRespond);
        var retried = await request.TryRespondAsync(outcome, optionId).WaitAsync(TimeSpan.FromSeconds(5), token);

        // Assert: the same request and session reach the peer, for both a choice and a cancellation.
        Assert.True(retried);
        Assert.False(request.CanRespond);
        Assert.True(client.IsConnected);
        Assert.Equal(new[] { PermissionHttpPeer.SessionId, PermissionHttpPeer.SessionId }, peer.ResponseSessions);
        Assert.All(peer.ResponseProtocols, protocol => Assert.Equal("HTTP/2", protocol));
        var response = Assert.Single(peer.AcceptedResponses);
        Assert.Equal(73, response.GetProperty("id").GetInt32());
        Assert.Equal(outcome, response.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        await client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5), token);
    }

    private sealed class PermissionHttpPeer : IAsyncDisposable
    {
        internal const string SessionId = "permission-session";
        private const string ConnectionId = "permission-connection";
        private readonly WebApplication _app;
        private readonly Channel<string> _events = Channel.CreateUnbounded<string>();
        private readonly CancellationTokenSource _stop = new();
        private int _responseCount;

        private PermissionHttpPeer(WebApplication app) => _app = app;

        internal string Url => _app.Urls.Single() + "/acp";
        internal TaskCompletionSource SessionReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentQueue<string> ResponseSessions { get; } = new();
        internal ConcurrentQueue<string> ResponseProtocols { get; } = new();
        internal ConcurrentQueue<JsonElement> AcceptedResponses { get; } = new();

        internal static async Task<PermissionHttpPeer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
                listener => listener.Protocols = HttpProtocols.Http2));
            var peer = new PermissionHttpPeer(builder.Build());
            peer._app.Run(peer.HandleAsync);
            await peer._app.StartAsync(TestContext.Current.CancellationToken);
            return peer;
        }

        internal ValueTask SendPermissionAsync(CancellationToken token)
            => _events.Writer.WriteAsync("""
                {"jsonrpc":"2.0","id":73,"method":"session/request_permission","params":{
                "sessionId":"permission-session","toolCall":{"toolCallId":"tool"},
                "options":[{"optionId":"allow","name":"Allow once","kind":"allow_once"}]}}
                """.ReplaceLineEndings(string.Empty), token);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _events.Writer.TryComplete();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _app.StopAsync(timeout.Token);
            await _app.DisposeAsync();
            _stop.Dispose();
        }

        private async Task HandleAsync(HttpContext context)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, context.RequestAborted);
            try
            {
                if (HttpMethods.IsDelete(context.Request.Method))
                {
                    context.Response.StatusCode = StatusCodes.Status202Accepted;
                    return;
                }
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    await StreamAsync(context, lifetime.Token);
                    return;
                }

                using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: lifetime.Token);
                var message = document.RootElement;
                if (message.TryGetProperty("method", out var method))
                {
                    var id = message.GetProperty("id").GetRawText();
                    var result = method.GetString() == "initialize"
                        ? "{\"protocolVersion\":1,\"agentInfo\":{\"name\":\"peer\",\"version\":\"1\"},\"agentCapabilities\":{}}"
                        : "{\"sessionId\":\"" + SessionId + "\"}";
                    context.Response.Headers["Acp-Connection-Id"] = ConnectionId;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + result + "}", lifetime.Token);
                    return;
                }

                var sessionId = context.Request.Headers["Acp-Session-Id"].ToString();
                ResponseSessions.Enqueue(sessionId);
                ResponseProtocols.Enqueue(context.Request.Protocol);
                if (Interlocked.Increment(ref _responseCount) == 1)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                }
                else if (sessionId != SessionId)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
                else
                {
                    AcceptedResponses.Enqueue(message.Clone());
                    context.Response.StatusCode = StatusCodes.Status202Accepted;
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
        }

        private async Task StreamAsync(HttpContext context, CancellationToken token)
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(": ready\n\n", token);
            await context.Response.Body.FlushAsync(token);
            if (context.Request.Headers["Acp-Session-Id"] != SessionId)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return;
            }

            SessionReady.TrySetResult();
            await foreach (var message in _events.Reader.ReadAllAsync(token))
            {
                await context.Response.WriteAsync("data: " + message + "\n\n", token);
                await context.Response.Body.FlushAsync(token);
            }
        }
    }
}
