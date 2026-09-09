using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpClientConnectionWriteTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateSessionAsync_DelayedWriteAfterReconnect_StaysOnOriginalConnection()
    {
        // Arrange
        using var peer = new ConnectionPeer();
        using var client = new AcpClient(peer);
        await client.InitializeAsync(Initialize(), TestToken);
        peer.DelaySessionWrite = true;
        var pending = client.CreateSessionAsync(NewSession(), TestToken);
        await peer.WriteStarted.Task.WaitAsync(TestToken);
        await client.DisconnectAsync();
        await client.InitializeAsync(Initialize(), TestToken);

        // Act
        peer.ReleaseWrite.TrySetResult();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TestToken));
        Assert.DoesNotContain(peer.Sent, static request => request.Method == "session/new");
        peer.DelaySessionWrite = false;
        var current = await client.CreateSessionAsync(NewSession(), TestToken);
        Assert.Equal("new-session", current.SessionId);
        Assert.Single(peer.Sent, static request => request.Method == "session/new");
    }

    [Fact]
    public async Task CreateSessionAsync_DelayedWriteAfterDispose_DoesNotSend()
    {
        // Arrange
        using var peer = new ConnectionPeer();
        using var client = new AcpClient(peer);
        await client.InitializeAsync(Initialize(), TestToken);
        peer.DelaySessionWrite = true;
        var pending = client.CreateSessionAsync(NewSession(), TestToken);
        await peer.WriteStarted.Task.WaitAsync(TestToken);

        // Act
        client.Dispose();
        peer.ReleaseWrite.TrySetResult();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TestToken));
        Assert.DoesNotContain(peer.Sent, static request => request.Method == "session/new");
    }

    [Fact]
    public async Task CreateSessionAsync_SynchronousPeerResponse_IsCorrelatedBeforeSendReturns()
    {
        // Arrange
        using var peer = new ConnectionPeer();
        using var client = new AcpClient(peer);
        await client.InitializeAsync(Initialize(), TestToken);

        // Act
        var result = await client.CreateSessionAsync(NewSession(), TestToken).WaitAsync(TestToken);

        // Assert
        Assert.Equal("new-session", result.SessionId);
        Assert.Single(peer.Sent, static request => request.Method == "session/new");
    }

    private static InitializeParams Initialize()
        => new(new ClientInfo("connection-peer", "1.0"), new ClientCapabilities());

    private static SessionNewParams NewSession()
        => new(Path.GetFullPath(Path.GetTempPath()), []);

    private sealed class ConnectionPeer : IAcpTransport
    {
        private readonly MessageParser _parser = new();

        public bool IsConnected { get; private set; }
        internal bool DelaySessionWrite { get; set; }
        internal TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<JsonRpcRequest> Sent { get; } = [];

        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task<bool> DisconnectAsync()
        {
            IsConnected = false;
            return Task.FromResult(true);
        }

        public async Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_parser.ParseMessage(message) is not JsonRpcRequest request) return true;
            if (DelaySessionWrite && request.Method == "session/new")
            {
                WriteStarted.TrySetResult();
                await ReleaseWrite.Task.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            Sent.Add(request);
            var result = request.Method == "initialize"
                ? JsonSerializer.Serialize(new InitializeResponse(
                    AcpProtocolVersion.V1, new AgentInfo("connection-agent", "1.0"), new AgentCapabilities()),
                    AcpJsonContext.Default.InitializeResponse)
                : "{\"sessionId\":\"new-session\"}";
            MessageReceived?.Invoke(this, new AcpTransportMessageReceivedEventArgs(
                "{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"result\":" + result + "}"));
            return true;
        }

        public void Dispose()
        {
            IsConnected = false;
            ReleaseWrite.TrySetResult();
        }
    }
}
