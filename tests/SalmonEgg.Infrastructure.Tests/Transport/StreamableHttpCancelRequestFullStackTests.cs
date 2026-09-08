using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
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
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>Real HTTP/2 POST and SSE traffic through the production HttpClient and both adapters.</summary>
public sealed class StreamableHttpCancelRequestFullStackTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallerCancellation_DuringOrAfterPost_SettlesOverSseAndKeepsConnectionUsable(bool duringPost)
    {
        await using var peer = await CancellationHttpPeer.StartAsync(duringPost);
        using var http = new StreamableHttpTransport(Log.Logger);
        var observed = new ObservedSendTransport(http);
        using var network = new NetworkTransportAdapter(observed, peer.Url);
        var probe = new CancellationTestProbe();
        using var client = new AcpClient(new DomainAcpTransportAdapter(network), probe);
        client.ErrorOccurred += probe.RecordError;
        await client.InitializeAsync(new InitializeParams(new ClientInfo("cancel-test", "1.0"), new ClientCapabilities()),
            TestContext.Current.CancellationToken);
        await peer.ConnectionStreamReady.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();

        var pending = client.CreateSessionAsync(new SessionNewParams(Path.GetFullPath(Path.GetTempPath()), null), cancellation.Token);
        await peer.FirstRequest.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (!duringPost)
        {
            await observed.SessionPostCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var notification = await peer.Cancellation.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        CancellationTestProbe.AssertMatchingNotification(await peer.FirstRequest.Task, notification);
        await probe.Settlement.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (duringPost)
        {
            await peer.FirstPostAborted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        var next = await client.CreateSessionAsync(new SessionNewParams(Path.GetFullPath(Path.GetTempPath()), null),
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("session-after-cancel", next.SessionId);
        var abandoned = client.CreateSessionAsync(new SessionNewParams(Path.GetFullPath(Path.GetTempPath()), null),
            TestContext.Current.CancellationToken);
        await peer.ThirdRequest.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await client.DisconnectAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        await peer.Terminated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(client.IsInitialized);
        Assert.False(network.IsConnected);
        Assert.Empty(probe.Errors);
    }

    // Observes completion of the actual production send, so "after POST" cannot accidentally cancel
    // during a slow response flush. All messages, tokens, responses and errors are passed unchanged.
    private sealed class ObservedSendTransport(ITransport inner) : ITransport
    {
        public TaskCompletionSource SessionPostCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IObservable<string> Messages => inner.Messages;
        public IObservable<TransportState> StateChanges => inner.StateChanges;
        public Task ConnectAsync(string url, CancellationToken ct) => inner.ConnectAsync(url, ct);
        public Task DisconnectAsync() => inner.DisconnectAsync();
        public async Task SendAsync(string message, CancellationToken ct)
        {
            await inner.SendAsync(message, ct);
            using var frame = JsonDocument.Parse(message);
            if (frame.RootElement.TryGetProperty("method", out var method) && method.GetString() == "session/new")
            {
                SessionPostCompleted.TrySetResult();
            }
        }
    }

    private sealed class CancellationHttpPeer : IAsyncDisposable
    {
        private const string ConnectionId = "cancel-http-connection";
        private readonly WebApplication _app;
        private readonly bool _holdFirstPost;
        private readonly Channel<string> _events = Channel.CreateUnbounded<string>();
        private readonly CancellationTokenSource _stop = new();
        private int _sessionRequestCount;

        private CancellationHttpPeer(WebApplication app, bool holdFirstPost)
        {
            _app = app;
            _holdFirstPost = holdFirstPost;
        }

        public string Url => _app.Urls.Single() + "/acp";
        public TaskCompletionSource ConnectionStreamReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<JsonElement> FirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<JsonElement> Cancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstPostAborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ThirdRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Terminated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<CancellationHttpPeer> StartAsync(bool holdFirstPost)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
                listener => listener.Protocols = HttpProtocols.Http2));
            var peer = new CancellationHttpPeer(builder.Build(), holdFirstPost);
            peer._app.Run(peer.HandleAsync);
            await peer._app.StartAsync(TestContext.Current.CancellationToken);
            return peer;
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _events.Writer.TryComplete();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _app.StopAsync(timeout.Token);
            await _app.DisposeAsync();
            _stop.Dispose();
        }

        private async Task HandleAsync(HttpContext context)
        {
            using var requestStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, context.RequestAborted);
            try
            {
                Assert.Equal("HTTP/2", context.Request.Protocol);
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    Assert.Equal(ConnectionId, context.Request.Headers["Acp-Connection-Id"]);
                    context.Response.ContentType = "text/event-stream";
                    await context.Response.WriteAsync(": connected\n\n", requestStop.Token);
                    await context.Response.Body.FlushAsync(requestStop.Token);
                    if (context.Request.Headers.ContainsKey("Acp-Session-Id"))
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, requestStop.Token);
                        return;
                    }
                    ConnectionStreamReady.TrySetResult();
                    await foreach (var frame in _events.Reader.ReadAllAsync(requestStop.Token))
                    {
                        await context.Response.WriteAsync("data: " + frame + "\n\n", requestStop.Token);
                        await context.Response.Body.FlushAsync(requestStop.Token);
                    }
                    return;
                }
                if (HttpMethods.IsDelete(context.Request.Method))
                {
                    Assert.Equal(ConnectionId, context.Request.Headers["Acp-Connection-Id"]);
                    context.Response.StatusCode = StatusCodes.Status202Accepted;
                    Terminated.TrySetResult();
                    return;
                }

                using var json = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: requestStop.Token);
                var frameRoot = json.RootElement;
                var method = frameRoot.GetProperty("method").GetString();
                if (method == "initialize")
                {
                    context.Response.Headers["Acp-Connection-Id"] = ConnectionId;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"jsonrpc\":\"2.0\",\"id\":" + frameRoot.GetProperty("id").GetRawText()
                        + ",\"result\":{\"protocolVersion\":1,\"agentInfo\":{\"name\":\"cancel-peer\",\"version\":\"1.0\"},\"agentCapabilities\":{}}}", requestStop.Token);
                    return;
                }
                Assert.Equal(ConnectionId, context.Request.Headers["Acp-Connection-Id"]);
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                if (method == CancelRequestParams.Method)
                {
                    Cancellation.TrySetResult(frameRoot.Clone());
                    await _events.Writer.WriteAsync("{\"jsonrpc\":\"2.0\",\"id\":"
                        + frameRoot.GetProperty("params").GetProperty("requestId").GetRawText()
                        + ",\"error\":{\"code\":-32800,\"message\":\"Cancelled\"}}", requestStop.Token);
                }
                else if (method == "session/new")
                {
                    var count = Interlocked.Increment(ref _sessionRequestCount);
                    if (count == 1)
                    {
                        FirstRequest.TrySetResult(frameRoot.Clone());
                        if (_holdFirstPost)
                        {
                            try { await Task.Delay(Timeout.InfiniteTimeSpan, requestStop.Token); }
                            finally { FirstPostAborted.TrySetResult(); }
                        }
                    }
                    else if (count == 2)
                    {
                        await _events.Writer.WriteAsync("{\"jsonrpc\":\"2.0\",\"id\":"
                            + frameRoot.GetProperty("id").GetRawText()
                            + ",\"result\":{\"sessionId\":\"session-after-cancel\"}}", requestStop.Token);
                    }
                    else
                    {
                        ThirdRequest.TrySetResult();
                    }
                }
            }
            catch (OperationCanceledException) when (requestStop.IsCancellationRequested)
            {
            }
        }
    }
}
