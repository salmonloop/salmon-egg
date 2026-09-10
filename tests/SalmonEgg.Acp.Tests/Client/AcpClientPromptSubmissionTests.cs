using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Observability;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpClientPromptSubmissionTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public async Task SendPromptAsync_CancelledBeforeTransportWrite_DoesNotRetainUnsentWork(int version)
    {
        // Arrange
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        using var peer = await PromptPeer.CreateAsync(version, () => caller.Cancel());

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => peer.PromptAsync(caller.Token));

        // Assert
        Assert.Empty(peer.PromptWrites);
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("session")?.PendingPrompts ?? 0);
        Assert.DoesNotContain(peer.Notifications, static notification => notification.Method == CancelRequestParams.Method);
        await peer.Client.CancelSessionAsync(new SessionCancelParams("session"), TestToken).WaitAsync(TestToken);
    }

    [Fact]
    public async Task SendPromptAsync_CancelledAfterWorkRegistration_DoesNotWaitForUnsentAcceptance()
    {
        // Arrange
        using var peer = await PromptPeer.CreateAsync(AcpProtocolVersion.V2);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        using var trace = new Activity("prompt-submission").Start();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AcpActivitySources.ClientName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => options.Parent.TraceId == trace.TraceId
                ? ActivitySamplingResult.AllData : ActivitySamplingResult.None,
            ActivityStarted = activity =>
            {
                if (activity.TraceId == trace.TraceId && activity.OperationName == "acp.request session/prompt") caller.Cancel();
            }
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => peer.PromptAsync(caller.Token));

        // Assert
        Assert.Empty(peer.PromptWrites);
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("session")!.PendingPrompts);
        await peer.Client.CancelSessionAsync(new SessionCancelParams("session"), TestToken).WaitAsync(TestToken);
    }

    [Fact]
    public async Task SendPromptAsync_UnsentContributionCancelled_PreservesOtherActiveWork()
    {
        // Arrange
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        var cancelSubmission = false;
        using var peer = await PromptPeer.CreateAsync(AcpProtocolVersion.V2, () =>
        {
            if (cancelSubmission) caller.Cancel();
        });
        var active = peer.PromptAsync(TestToken);
        var activeRequest = Assert.Single(peer.PromptWrites);
        peer.Accept(activeRequest);
        peer.State("running");

        // Act
        cancelSubmission = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => peer.PromptAsync(caller.Token));

        // Assert
        Assert.Single(peer.PromptWrites);
        var work = Assert.IsType<SessionWorkSnapshot>(peer.Client.GetSessionWorkSnapshot("session"));
        Assert.Equal(1, work.PendingPrompts);
        Assert.Equal(1, work.AcceptedPrompts);
        Assert.IsType<RunningSessionWorkState>(work.State);
        peer.State("idle");
        await active.WaitAsync(TestToken);
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public async Task SendPromptAsync_CancelledDuringWrite_RetainsCorrelationUntilPeerResponds(int version)
    {
        // Arrange
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        using var peer = await PromptPeer.CreateAsync(version);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.CompletePromptWrite = async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TestToken);
            token.ThrowIfCancellationRequested();
            return true;
        };
        var pending = peer.PromptAsync(caller.Token);
        await entered.Task.WaitAsync(TestToken);

        // Act
        try
        {
            caller.Cancel();
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TestToken));

            // Assert
            Assert.Equal(1, peer.Client.GetSessionWorkSnapshot("session")!.PendingPrompts);
            peer.Accept(Assert.Single(peer.PromptWrites));
            if (version == AcpProtocolVersion.V2) peer.State("idle");
            Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("session")!.PendingPrompts);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, false)]
    [InlineData(AcpProtocolVersion.V1, true)]
    [InlineData(AcpProtocolVersion.V2, false)]
    [InlineData(AcpProtocolVersion.V2, true)]
    public async Task SendPromptAsync_WriteReturnsFalse_RetainsPossiblePeerWork(int version, bool acknowledgedDuringWrite)
    {
        // Arrange
        using var peer = await PromptPeer.CreateAsync(version);
        peer.CompletePromptWrite = (request, _) =>
        {
            if (acknowledgedDuringWrite) peer.Accept(request);
            return Task.FromResult(false);
        };

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => peer.PromptAsync(TestToken));

        // Assert
        var request = Assert.Single(peer.PromptWrites);
        Assert.Equal(acknowledgedDuringWrite && version == AcpProtocolVersion.V1 ? 0 : 1,
            peer.Client.GetSessionWorkSnapshot("session")!.PendingPrompts);
        if (!acknowledgedDuringWrite) peer.Accept(request);
        if (version == AcpProtocolVersion.V2)
        {
            Assert.Equal(1, peer.Client.GetSessionWorkSnapshot("session")!.AcceptedPrompts);
            peer.State("idle");
        }
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("session")!.PendingPrompts);
        peer.Accept(request);
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("session")!.PendingPrompts);
    }

    [Fact]
    public async Task SendPromptAsync_WriteThrowsAfterStart_RetainsPeerRejectionCorrelation()
    {
        // Arrange
        using var peer = await PromptPeer.CreateAsync(AcpProtocolVersion.V2);
        var failure = new IOException("The write outcome is unknown.");
        peer.CompletePromptWrite = (_, _) => Task.FromException<bool>(failure);

        // Act
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => peer.PromptAsync(TestToken)));

        // Assert
        Assert.Equal(1, peer.Client.GetSessionWorkSnapshot("session")!.PendingPrompts);
        peer.Reject(Assert.Single(peer.PromptWrites));
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("session")!.PendingPrompts);
    }

    [Fact]
    public async Task SendPromptAsync_WriteOutcomeUnknown_DisconnectReleasesRetainedWork()
    {
        // Arrange
        using var peer = await PromptPeer.CreateAsync(AcpProtocolVersion.V2);
        peer.CompletePromptWrite = (_, _) => Task.FromResult(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => peer.PromptAsync(TestToken));
        Assert.Equal(1, peer.Client.GetSessionWorkSnapshot("session")!.PendingPrompts);

        // Act
        await peer.Client.DisconnectAsync();

        // Assert
        Assert.Null(peer.Client.GetSessionWorkSnapshot("session"));
    }

    private sealed class PromptPeer : IAcpTransport
    {
        private readonly MessageParser _parser = new();
        private readonly int _version;

        private PromptPeer(int version, Action? validateSession)
        {
            _version = version;
            var store = new Mock<IAcpClientSessionStore>();
            store.Setup(value => value.ContainsSession("session"))
                .Callback(() => validateSession?.Invoke()).Returns(true);
            store.Setup(value => value.CancelSessionAsync("session")).ReturnsAsync(true);
            Client = new AcpClient(this, sessionStore: store.Object);
        }

        internal AcpClient Client { get; }
        public bool IsConnected { get; private set; }
        internal ConcurrentQueue<JsonRpcRequest> PromptWrites { get; } = new();
        internal ConcurrentQueue<JsonRpcNotification> Notifications { get; } = new();
        internal Func<JsonRpcRequest, CancellationToken, Task<bool>>? CompletePromptWrite { get; set; }

        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            var parsed = _parser.ParseMessage(message);
            if (parsed is JsonRpcNotification notification) Notifications.Enqueue(notification);
            if (parsed is not JsonRpcRequest request) return Task.FromResult(true);
            if (request.Method == "initialize")
            {
                var response = new InitializeResponse(_version, new AgentInfo("submission-peer", "1.0"), new AgentCapabilities());
                Reply(request, JsonSerializer.Serialize(response, AcpWireFormat.For(_version).TypeInfo<InitializeResponse>()));
            }
            else if (request.Method == "session/prompt")
            {
                PromptWrites.Enqueue(request);
                return CompletePromptWrite?.Invoke(request, cancellationToken) ?? Task.FromResult(true);
            }
            return Task.FromResult(true);
        }

        public void Dispose()
        {
            Client.Dispose();
            IsConnected = false;
        }

        internal static async Task<PromptPeer> CreateAsync(int version, Action? validateSession = null)
        {
            var peer = new PromptPeer(version, validateSession);
            var request = new InitializeParams(new ClientInfo("submission-test", "1.0"), new ClientCapabilities())
            {
                ProtocolVersion = version
            };
            if (version == AcpProtocolVersion.V2) await peer.Client.InitializeDraftAsync(request, TestToken);
            else await peer.Client.InitializeAsync(request, TestToken);
            return peer;
        }

        internal Task<SessionPromptResponse> PromptAsync(CancellationToken token)
            => Client.SendPromptAsync(new SessionPromptParams("session", []), token);

        internal void Accept(JsonRpcRequest request)
            => Reply(request, _version == AcpProtocolVersion.V2 ? "{}" : "{\"stopReason\":\"end_turn\"}");

        internal void Reject(JsonRpcRequest request)
            => Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + request.Id
                + ",\"error\":{\"code\":-32602,\"message\":\"prompt rejected\"}}");

        internal void State(string state)
            => Deliver("{\"jsonrpc\":\"2.0\",\"method\":\"session/update\",\"params\":{\"sessionId\":\"session\","
                + "\"update\":{\"sessionUpdate\":\"state_update\",\"state\":\"" + state + "\",\"stopReason\":\"end_turn\"}}}");

        private void Reply(JsonRpcRequest request, string result)
            => Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"result\":" + result + "}");

        private void Deliver(string message)
            => MessageReceived?.Invoke(this, new AcpTransportMessageReceivedEventArgs(message));
    }
}
