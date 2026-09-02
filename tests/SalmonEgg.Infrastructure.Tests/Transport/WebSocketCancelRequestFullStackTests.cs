using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Network;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>
/// Exercises the complete WebSocket production chain with a real loopback WebSocket server:
/// socket → network adapter → domain adapter → ACP client. Unlike a mock, the server sees the
/// actual masked client frames after the WebSocket library has serialized them.
/// </summary>
public sealed class WebSocketCancelRequestFullStackTests
{
    [Fact]
    public async Task CallerCancelsDispatchedRequest_SendsMatchingCancelNotificationOverWebSocket()
    {
        await using var peer = new CancelRequestWebSocketPeer();
        using var socket = new WebSocketTransport(Log.Logger, connectTimeout: TimeSpan.FromSeconds(10));
        using var network = new NetworkTransportAdapter(socket, peer.Url);
        using var transport = new DomainAcpTransportAdapter(network);
        using var client = new AcpClient(transport);

        Assert.True(await network.ConnectAsync(TestContext.Current.CancellationToken));
        await client.InitializeAsync(
            new InitializeParams(new ClientInfo("test", "1.0"), new ClientCapabilities())
            {
                ProtocolVersion = AcpProtocolVersion.V1
            },
            TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        var request = client.CreateSessionAsync(
            new SessionNewParams(Path.GetFullPath(Path.GetTempPath()), null),
            cancellation.Token);
        await peer.WaitForMethodAsync("session/new", TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await peer.WaitForMethodAsync(CancelRequestParams.Method, TestContext.Current.CancellationToken);

        var original = peer.SingleFrame("session/new");
        var cancel = peer.SingleFrame(CancelRequestParams.Method);
        Assert.False(cancel.TryGetProperty("id", out _));
        Assert.Equal(
            original.GetProperty("id").GetRawText(),
            cancel.GetProperty("params").GetProperty("requestId").GetRawText());

        await network.DisconnectAsync();
    }

    private sealed class CancelRequestWebSocketPeer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentQueue<JsonElement> _frames = new();
        private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serveTask;

        public CancelRequestWebSocketPeer()
        {
            var port = ReserveLoopbackPort();
            Url = $"ws://127.0.0.1:{port}/acp/";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/acp/");
            _listener.Start();
            _serveTask = ServeAsync();
        }

        public string Url { get; }

        public async Task WaitForMethodAsync(string method, CancellationToken cancellationToken)
        {
            await _connected.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                foreach (var frame in _frames)
                {
                    if (frame.TryGetProperty("method", out var candidate)
                        && string.Equals(candidate.GetString(), method, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                await Task.Delay(20, cancellationToken);
            }

            throw new TimeoutException($"Timed out waiting for WebSocket peer to receive '{method}'.");
        }

        public JsonElement SingleFrame(string method)
        {
            JsonElement? result = null;
            foreach (var frame in _frames)
            {
                if (frame.TryGetProperty("method", out var candidate)
                    && string.Equals(candidate.GetString(), method, StringComparison.Ordinal))
                {
                    Assert.Null(result);
                    result = frame;
                }
            }

            return Assert.IsType<JsonElement>(result);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Close();
            try { await _serveTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _stop.Dispose();
        }

        private async Task ServeAsync()
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(_stop.Token).ConfigureAwait(false);
                Assert.True(context.Request.IsWebSocketRequest);
                var webSocketContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                using var socket = webSocketContext.WebSocket;
                _connected.TrySetResult();
                var buffer = new byte[16 * 1024];

                while (!_stop.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var received = await socket.ReceiveAsync(buffer, _stop.Token).ConfigureAwait(false);
                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    Assert.True(received.EndOfMessage);
                    var message = Encoding.UTF8.GetString(buffer, 0, received.Count);
                    using var document = JsonDocument.Parse(message);
                    var frame = document.RootElement.Clone();
                    _frames.Enqueue(frame);

                    if (frame.TryGetProperty("method", out var method)
                        && string.Equals(method.GetString(), "initialize", StringComparison.Ordinal))
                    {
                        var id = frame.GetProperty("id").GetRawText();
                        var response = "{\"jsonrpc\":\"2.0\",\"id\":" + id
                            + ",\"result\":{\"protocolVersion\":1,\"agentInfo\":{\"name\":\"test-agent\",\"version\":\"1.0\"},\"agentCapabilities\":{}}}";
                        var bytes = Encoding.UTF8.GetBytes(response);
                        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, _stop.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (HttpListenerException) when (_stop.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private static int ReserveLoopbackPort()
        {
            using var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            return ((IPEndPoint)reservation.LocalEndpoint).Port;
        }
    }
}
