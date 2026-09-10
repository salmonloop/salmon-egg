using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpClientParseErrorOwnershipTests
{
    [Fact]
    public async Task ParseFailure_InitializeFromPreHandshakeErrorObserver_DoesNotReplyOnNewConnection()
    {
        // Arrange
        using var peer = new ReconnectingPeer();
        await peer.ConnectAsync(TestContext.Current.CancellationToken);
        using var client = new AcpClient(peer);
        var initialize = new InitializeParams(new ClientInfo("test", "1"), new ClientCapabilities());
        client.ErrorOccurred += (_, _) =>
            Assert.True(client.InitializeAsync(initialize, TestContext.Current.CancellationToken).IsCompletedSuccessfully);

        // Act
        peer.Deliver("{");

        // Assert
        Assert.True(client.IsInitialized);
        Assert.Empty(peer.Replies);
    }

    [Fact]
    public async Task ParseFailure_CurrentConnection_RepliesWithExplicitNullId()
    {
        // Arrange
        using var peer = new ReconnectingPeer();
        using var client = new AcpClient(peer);
        var initialize = new InitializeParams(new ClientInfo("test", "1"), new ClientCapabilities());
        await client.InitializeAsync(initialize, TestContext.Current.CancellationToken);

        // Act
        peer.Deliver("{");

        // Assert
        var reply = Assert.Single(peer.Replies);
        Assert.Equal(1, reply.Generation);
        using var document = JsonDocument.Parse(reply.Message);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("id").ValueKind);
        Assert.Equal(JsonRpcErrorCode.ParseError, document.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ParseFailure_ReconnectFromErrorObserver_DoesNotReplyOnReplacementConnection()
    {
        // Arrange: synchronous handshakes make observer reentry deterministic without sleeps.
        using var peer = new ReconnectingPeer();
        using var client = new AcpClient(peer);
        var initialize = new InitializeParams(new ClientInfo("test", "1"), new ClientCapabilities());
        await client.InitializeAsync(initialize, TestContext.Current.CancellationToken);
        var reentered = false;
        client.ErrorOccurred += (_, _) =>
        {
            if (reentered) return;
            reentered = true;
            Assert.True(client.DisconnectAsync().IsCompletedSuccessfully);
            Assert.True(client.InitializeAsync(initialize, TestContext.Current.CancellationToken).IsCompletedSuccessfully);
        };

        // Act
        peer.Deliver("{");

        // Assert
        Assert.True(reentered);
        Assert.Equal(2, peer.Generation);
        Assert.DoesNotContain(peer.Replies, static reply => reply.Generation == 2);
    }

    private sealed class ReconnectingPeer : IAcpTransport
    {
        public bool IsConnected { get; private set; }
        public int Generation { get; private set; }
        public List<(int Generation, string Message)> Replies { get; } = [];
        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Generation++;
            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task<bool> DisconnectAsync()
        {
            IsConnected = false;
            return Task.FromResult(true);
        }

        public Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.TryGetProperty("method", out var method) && method.GetString() == "initialize")
            {
                Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + root.GetProperty("id").GetRawText()
                    + ",\"result\":{\"protocolVersion\":1,\"agentInfo\":{\"name\":\"peer\",\"version\":\"1\"},\"agentCapabilities\":{}}}");
            }
            else if (root.TryGetProperty("error", out _))
            {
                Replies.Add((Generation, message));
            }
            return Task.FromResult(true);
        }

        public void Deliver(string message)
            => MessageReceived?.Invoke(this, new AcpTransportMessageReceivedEventArgs(message));

        public void Dispose() => IsConnected = false;
    }
}
