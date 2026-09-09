using System.Collections.Concurrent;
using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpClientSessionWorkTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendPromptAsync_V2Acknowledgement_DoesNotFinishForegroundWork()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var observed = new List<SessionUpdate>();
        peer.Client.SessionUpdateReceived += (_, update) => observed.Add(Assert.IsAssignableFrom<SessionUpdate>(update.Update));

        // Act
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");
        peer.State("one", "requires_action");

        // Assert
        Assert.False(pending.IsCompleted);
        var snapshot = Assert.IsType<SessionWorkSnapshot>(peer.Client.GetSessionWorkSnapshot("one"));
        Assert.IsType<RequiresActionSessionWorkState>(snapshot.State);
        Assert.Equal(1, snapshot.AcceptedPrompts);

        peer.State("one", "running");
        peer.State("one", "idle", "max_tokens");
        var completed = await pending.WaitAsync(TestToken);
        Assert.Equal(StopReason.MaxTokens, completed.StopReason);
        Assert.True(completed.HasStopReason);
        Assert.Equal(4, observed.Count);
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("one")!.PendingPrompts);
    }

    [Fact]
    public async Task SendPromptAsync_V2BackToBackAcknowledgementAndIdle_ObservesCompletion()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        peer.OnPrompt = request =>
        {
            peer.Reply(request, "{}");
            peer.State("one", "running");
            peer.State("one", "idle", "end_turn");
        };

        // Act
        var response = await peer.PromptAsync("one").WaitAsync(TestToken);

        // Assert
        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("one")!.PendingPrompts);
    }

    [Fact]
    public async Task SendPromptAsync_CompletionBeforeDelayedAcknowledgement_WaitsForAcceptance()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");

        // Act
        peer.State("one", "running");
        peer.State("one", "idle", "refusal");

        // Assert
        Assert.False(pending.IsCompleted);
        peer.ReplyPrompt("one", "{}");
        Assert.Equal(StopReason.Refusal, (await pending.WaitAsync(TestToken)).StopReason);
    }

    [Fact]
    public async Task SendPromptAsync_SetupIdleBeforeAcceptance_DoesNotFinishNewWork()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        peer.State("one", "idle");
        var pending = peer.PromptAsync("one");

        // Act
        peer.State("one", "idle");
        peer.ReplyPrompt("one", "{}");

        // Assert
        Assert.False(pending.IsCompleted);
        peer.State("one", "running");
        peer.State("one", "idle", "end_turn");
        Assert.Equal(StopReason.EndTurn, (await pending.WaitAsync(TestToken)).StopReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("123")]
    public async Task SendPromptAsync_IdleWithoutUsableReason_CompletesWithoutInventingReason(string? rawReason)
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");

        // Act
        peer.Update("one", "{\"sessionUpdate\":\"state_update\",\"state\":\"idle\""
            + (rawReason is null ? "" : ",\"stopReason\":" + rawReason) + "}");

        // Assert
        var response = await pending.WaitAsync(TestToken);
        Assert.False(response.HasStopReason);
        Assert.NotEqual(StopReason.EndTurn, response.StopReason);
        Assert.Null(Assert.IsType<IdleSessionWorkState>(peer.Client.GetSessionWorkSnapshot("one")!.State).StopReason);
    }

    [Fact]
    public async Task SendPromptAsync_UnknownStateAndStopReason_PreservesPeerValues()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");

        // Act
        peer.State("one", "_paused");

        // Assert
        Assert.False(pending.IsCompleted);
        Assert.Equal("_paused", Assert.IsType<CustomSessionWorkState>(peer.Client.GetSessionWorkSnapshot("one")!.State).State);
        peer.State("one", "idle", "future_stop");
        Assert.Equal("future_stop", (await pending.WaitAsync(TestToken)).StopReason.Value);
    }

    [Fact]
    public async Task SessionUpdates_UnsolicitedStateBeforePrompt_PreservesStateAndBackgroundUpdates()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var observed = new List<SessionUpdate>();
        peer.Client.SessionUpdateReceived += (_, update) => observed.Add(Assert.IsAssignableFrom<SessionUpdate>(update.Update));

        // Act
        peer.State("one", "running");
        peer.State("two", "requires_action");
        peer.State("one", "idle");
        peer.Update("one", "{\"sessionUpdate\":\"agent_message_chunk\",\"messageId\":\"background\",\"content\":{\"type\":\"text\",\"text\":\"background result\"}}");

        // Assert
        Assert.IsType<IdleSessionWorkState>(peer.Client.GetSessionWorkSnapshot("one")!.State);
        Assert.IsType<RequiresActionSessionWorkState>(peer.Client.GetSessionWorkSnapshot("two")!.State);
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("one")!.PendingPrompts);
        Assert.IsType<AgentMessageUpdate>(observed[^1]);
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task SendPromptAsync_TwoSessionsAndTwoAcceptedPrompts_CompletesOnlyMatchingWork()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var first = peer.PromptAsync("one");
        var second = peer.PromptAsync("two");
        peer.ReplyPrompt("one", "{}");
        peer.ReplyPrompt("two", "{}");
        peer.State("one", "running");
        peer.State("two", "running");
        var contribution = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");

        // Act
        peer.State("one", "idle", "end_turn");

        // Assert
        Assert.Equal(StopReason.EndTurn, (await first.WaitAsync(TestToken)).StopReason);
        Assert.Equal(StopReason.EndTurn, (await contribution.WaitAsync(TestToken)).StopReason);
        Assert.False(second.IsCompleted);
        peer.State("two", "idle", "refusal");
        Assert.Equal(StopReason.Refusal, (await second.WaitAsync(TestToken)).StopReason);
    }

    [Fact]
    public async Task CancelSessionAsync_ActiveV2Work_AcceptsTrailingUpdatesAndWaitsForCancelledIdle()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");
        var observed = new List<SessionUpdate>();
        peer.Client.SessionUpdateReceived += (_, update) => observed.Add(Assert.IsAssignableFrom<SessionUpdate>(update.Update));

        // Act
        var cancel = peer.Client.CancelSessionAsync(new SessionCancelParams("one"), TestToken);
        peer.Update("one", "{\"sessionUpdate\":\"agent_message_chunk\",\"messageId\":\"trailing\",\"content\":{\"type\":\"text\",\"text\":\"last bytes\"}}");

        // Assert
        Assert.False(cancel.IsCompleted);
        Assert.False(pending.IsCompleted);
        Assert.True(peer.Client.GetSessionWorkSnapshot("one")!.CancellationRequested);
        Assert.IsType<AgentMessageUpdate>(Assert.Single(observed));
        peer.State("one", "idle", "cancelled");
        await cancel.WaitAsync(TestToken);
        Assert.Equal(StopReason.Cancelled, (await pending.WaitAsync(TestToken)).StopReason);
        Assert.False(peer.Client.GetSessionWorkSnapshot("one")!.CancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelSessionAsync_NoActiveWork_DoesNotWaitForAnUnpromisedIdle(bool observedIdle)
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        if (observedIdle) peer.State("one", "idle");

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("one"), TestToken).WaitAsync(TestToken);

        // Assert
        Assert.Contains(peer.Sent, message => message is JsonRpcNotification { Method: "session/cancel" });
        Assert.False(peer.Client.GetSessionWorkSnapshot("one")!.CancellationRequested);
    }

    [Fact]
    public async Task CancelSessionAsync_UnsolicitedWork_WaitsForMatchingIdle()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        peer.State("one", "requires_action");

        // Act
        var cancel = peer.Client.CancelSessionAsync(new SessionCancelParams("one"), TestToken);
        peer.State("two", "idle", "cancelled");

        // Assert
        Assert.False(cancel.IsCompleted);
        peer.State("one", "idle", "cancelled");
        await cancel.WaitAsync(TestToken);
    }

    [Fact]
    public async Task SendPromptAsync_CallerCancelsAfterAcceptance_DoesNotPretendAgentStopped()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        using var caller = new CancellationTokenSource();
        var pending = peer.PromptAsync("one", caller.Token);
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");

        // Act
        caller.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.IsType<RunningSessionWorkState>(peer.Client.GetSessionWorkSnapshot("one")!.State);
        peer.State("one", "idle", "cancelled");
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("one")!.PendingPrompts);
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task SendPromptAsync_CallerCancelsBeforeAcceptance_LateResponseStillSettlesWork()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        using var caller = new CancellationTokenSource();
        var pending = peer.PromptAsync("one", caller.Token);

        // Act
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");
        peer.State("one", "idle", "cancelled");

        // Assert
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("one")!.PendingPrompts);
        Assert.Empty(peer.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectAsync_AcceptedWorkOrPendingCancellation_ReleasesAllWaiters(bool cancelFirst)
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");
        var cancel = cancelFirst ? peer.Client.CancelSessionAsync(new SessionCancelParams("one"), TestToken) : null;

        // Act
        await peer.Client.DisconnectAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        if (cancel is not null) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancel);
        Assert.Null(peer.Client.GetSessionWorkSnapshot("one"));
    }

    [Fact]
    public async Task TransportError_AcceptedWork_FaultsWithConnectionFailure()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");

        // Act
        peer.DropConnection();

        // Assert
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Contains("disconnected", failure.Message, StringComparison.Ordinal);
        Assert.Null(peer.Client.GetSessionWorkSnapshot("one"));
    }

    [Fact]
    public async Task SessionUpdate_QueuedOldConnectionHandler_CannotCompleteReconnectedSession()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var oldDelivery = peer.CaptureDelivery();
        var oldPrompt = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        await peer.Client.DisconnectAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldPrompt);
        await peer.InitializeAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");

        // Act
        oldDelivery(peer.StateMessage("one", "idle", "cancelled"));

        // Assert
        Assert.False(pending.IsCompleted);
        Assert.IsType<RunningSessionWorkState>(peer.Client.GetSessionWorkSnapshot("one")!.State);
        peer.State("one", "idle", "end_turn");
        Assert.Equal(StopReason.EndTurn, (await pending.WaitAsync(TestToken)).StopReason);
    }

    [Fact]
    public async Task CloseSessionAsync_ActiveWork_ReleasesWaiterAndRejectsStaleSessionUpdates()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");

        // Act
        await peer.Client.CloseSessionAsync(new SessionCloseParams("one"), TestToken);
        peer.State("one", "running");

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(peer.Client.GetSessionWorkSnapshot("one"));
    }

    [Theory]
    [InlineData("end_turn")]
    [InlineData("cancelled")]
    public async Task SendPromptAsync_V1Response_KeepsCompletionAndMetadataContract(string stopReason)
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync(AcpProtocolVersion.V1);
        var pending = peer.PromptAsync("one");
        peer.State("one", "idle", "refusal");
        Assert.False(pending.IsCompleted);

        // Act
        peer.ReplyPrompt("one", "{\"stopReason\":\"" + stopReason + "\",\"_meta\":{\"original\":true}}");

        // Assert
        var response = await pending.WaitAsync(TestToken);
        Assert.Equal(stopReason, response.StopReason.Value);
        Assert.True(response.HasStopReason);
        Assert.True(Assert.IsType<JsonElement>(response.Meta!["original"]).GetBoolean());
    }

    [Fact]
    public async Task InitializeAsync_PublicV2Entry_StillRejectsBeforeTransportActivity()
    {
        // Arrange
        using var peer = new ProtocolPeer(AcpProtocolVersion.V2);

        // Act
        var failure = await Assert.ThrowsAsync<AcpException>(() => peer.Client.InitializeAsync(peer.InitializeParams(), TestToken));

        // Assert
        Assert.Equal(JsonRpcErrorCode.ProtocolVersionMismatch, failure.ErrorCode);
        Assert.Equal(0, peer.ConnectCalls);
        Assert.Empty(peer.Sent);
    }

    [Theory]
    [InlineData("running")]
    [InlineData("requires_action")]
    [InlineData("idle")]
    public async Task ResumeSessionAsync_StateBeforeResponse_RemainsAuthoritativeWithoutPrompt(string state)
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        peer.BeforeSessionResponse = request =>
        {
            if (request.Method == "session/resume") peer.State("resumed", state);
        };

        // Act
        await peer.Client.ResumeSessionAsync(new SessionResumeParams("resumed", "/workspace", []), TestToken);

        // Assert
        var snapshot = Assert.IsType<SessionWorkSnapshot>(peer.Client.GetSessionWorkSnapshot("resumed"));
        Assert.Equal(state, snapshot.State!.State);
        Assert.Equal(0, snapshot.PendingPrompts);
    }

    [Fact]
    public async Task SendPromptAsync_RejectedAfterStateUpdates_DoesNotReportSuccessfulCompletion()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");
        peer.State("one", "running");
        peer.State("one", "idle", "end_turn");

        // Act
        peer.RejectPrompt("one", JsonRpcErrorCode.MethodNotAllowed);

        // Assert
        var error = await Assert.ThrowsAsync<AcpException>(() => pending);
        Assert.Equal(JsonRpcErrorCode.MethodNotAllowed, error.ErrorCode);
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("one")!.PendingPrompts);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    public async Task SendPromptAsync_MalformedAcknowledgement_FailsWithoutLeakingPendingWork(string result)
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");

        // Act
        peer.ReplyPrompt("one", result);

        // Assert
        await Assert.ThrowsAsync<AcpException>(() => pending);
        Assert.Equal(0, peer.Client.GetSessionWorkSnapshot("one")!.PendingPrompts);
    }

    [Fact]
    public async Task SendPromptAsync_UnacceptedContribution_DoesNotInheritPreviousIdle()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var first = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");
        var contribution = peer.PromptAsync("one");

        // Act
        peer.State("one", "idle", "end_turn");
        await first.WaitAsync(TestToken);
        peer.State("one", "running");
        peer.ReplyPrompt("one", "{}");

        // Assert
        Assert.False(contribution.IsCompleted);
        peer.State("one", "idle", "max_tokens");
        Assert.Equal(StopReason.MaxTokens, (await contribution.WaitAsync(TestToken)).StopReason);
    }

    [Fact]
    public async Task SendPromptAsync_DisposedAfterAcceptance_ReleasesWaiter()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");

        // Act
        peer.Client.Dispose();

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Null(peer.Client.GetSessionWorkSnapshot("one"));
    }

    [Fact]
    public async Task CancelSessionAsync_CallerAbandonsWait_LaterIdleStillCompletesWork()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");
        using var caller = new CancellationTokenSource();
        var cancel = peer.Client.CancelSessionAsync(new SessionCancelParams("one"), caller.Token);

        // Act
        caller.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancel);
        Assert.False(pending.IsCompleted);
        peer.State("one", "idle", "cancelled");
        Assert.Equal(StopReason.Cancelled, (await pending.WaitAsync(TestToken)).StopReason);
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task SessionUpdate_MutatedSubscriberMetadata_DoesNotChangeOwnedSnapshot()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        peer.Client.SessionUpdateReceived += (_, args) =>
        {
            if (args.Update is StateSessionUpdate state) state.State.Meta!["owned"] = false;
        };

        // Act
        peer.Update("one", "{\"sessionUpdate\":\"state_update\",\"state\":\"idle\",\"_meta\":{\"owned\":true}}");

        // Assert
        var snapshot = peer.Client.GetSessionWorkSnapshot("one")!;
        Assert.True(Assert.IsType<JsonElement>(snapshot.State!.Meta!["owned"]).GetBoolean());
        snapshot.State.Meta["owned"] = false;
        Assert.True(Assert.IsType<JsonElement>(peer.Client.GetSessionWorkSnapshot("one")!.State!.Meta!["owned"]).GetBoolean());
    }

    [Fact]
    public async Task CancelSessionAsync_SendReturnsFalse_FailsWithoutInventingCompletion()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");
        peer.OnCancel = () => Task.FromResult(false);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => peer.Client.CancelSessionAsync(new SessionCancelParams("one"), TestToken));

        // Assert
        Assert.False(pending.IsCompleted);
        Assert.IsType<RunningSessionWorkState>(peer.Client.GetSessionWorkSnapshot("one")!.State);
        peer.State("one", "idle", "end_turn");
        await pending.WaitAsync(TestToken);
    }

    [Fact]
    public async Task CancelSessionAsync_WriteFinishesAfterReconnect_DoesNotCancelReplacementState()
    {
        // Arrange
        using var peer = await ProtocolPeer.CreateAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");
        var send = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.OnCancel = () => send.Task;
        var cancel = peer.Client.CancelSessionAsync(new SessionCancelParams("one"), TestToken);
        await peer.Client.DisconnectAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await peer.InitializeAsync();
        var replacement = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");

        // Act
        send.TrySetResult(true);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancel);
        Assert.False(peer.Client.GetSessionWorkSnapshot("one")!.CancellationRequested);
        Assert.False(replacement.IsCompleted);
        peer.State("one", "idle", "end_turn");
        await replacement.WaitAsync(TestToken);
    }

    [Fact]
    public async Task InitializeDraftAsync_HandshakeQueuedCallback_CannotFinishReconnectedWork()
    {
        // Arrange
        using var peer = new ProtocolPeer(AcpProtocolVersion.V2);
        Action<string>? oldHandshake = null;
        peer.BeforeInitializeResponse = _ => oldHandshake ??= peer.CaptureDelivery();
        await peer.InitializeAsync();
        await peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
        await peer.Client.DisconnectAsync();
        await peer.InitializeAsync();
        var pending = peer.PromptAsync("one");
        peer.ReplyPrompt("one", "{}");
        peer.State("one", "running");

        // Act
        oldHandshake!(peer.StateMessage("one", "idle", "cancelled"));

        // Assert
        Assert.False(pending.IsCompleted);
        Assert.IsType<RunningSessionWorkState>(peer.Client.GetSessionWorkSnapshot("one")!.State);
        peer.State("one", "idle", "end_turn");
        await pending.WaitAsync(TestToken);
    }

    private sealed class ProtocolPeer : IAcpTransport
    {
        private readonly MessageParser _parser = new();
        private readonly int _version;
        private readonly Dictionary<string, Queue<JsonRpcRequest>> _prompts = new(StringComparer.Ordinal);
        private int _sessionNumber;

        internal ProtocolPeer(int version)
        {
            _version = version;
            Client = new AcpClient(this);
            Client.ErrorOccurred += (_, error) => Errors.Enqueue(error);
        }

        public bool IsConnected { get; private set; }
        internal int ConnectCalls { get; private set; }
        internal AcpClient Client { get; }
        internal ConcurrentQueue<JsonRpcMessage> Sent { get; } = new();
        internal ConcurrentQueue<string> Errors { get; } = new();
        internal Action<JsonRpcRequest>? OnPrompt { get; set; }
        internal Action<JsonRpcRequest>? BeforeSessionResponse { get; set; }
        internal Action<JsonRpcRequest>? BeforeInitializeResponse { get; set; }
        internal Func<Task<bool>>? OnCancel { get; set; }

        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred;

        internal static async Task<ProtocolPeer> CreateAsync(int version = AcpProtocolVersion.V2)
        {
            var peer = new ProtocolPeer(version);
            await peer.InitializeAsync();
            await peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
            await peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
            return peer;
        }

        internal InitializeParams InitializeParams() => new(new ClientInfo("test-client", "1.0"), new ClientCapabilities())
        {
            ProtocolVersion = _version
        };

        internal Task<InitializeResponse> InitializeAsync()
            => _version == AcpProtocolVersion.V2
                ? Client.InitializeDraftAsync(InitializeParams(), TestToken)
                : Client.InitializeAsync(InitializeParams(), TestToken);

        internal Task<SessionPromptResponse> PromptAsync(string sessionId, CancellationToken? callerToken = null)
            => Client.SendPromptAsync(new SessionPromptParams(sessionId, [new TextContentBlock("hello")]), callerToken ?? TestToken);

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
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
            Sent.Enqueue(parsed);
            if (parsed is JsonRpcNotification { Method: "session/cancel" } && OnCancel is not null) return OnCancel();
            if (parsed is not JsonRpcRequest request) return Task.FromResult(true);
            switch (request.Method)
            {
                case "initialize":
                    BeforeInitializeResponse?.Invoke(request);
                    var response = new InitializeResponse(_version, new AgentInfo("deterministic-peer", "1.0"), new AgentCapabilities
                    {
                        SessionCapabilities = new SessionCapabilities
                        {
                            Close = new SessionCloseCapabilities(),
                            Resume = new SessionResumeCapabilities()
                        }
                    });
                    Reply(request, JsonSerializer.Serialize(response, AcpWireFormat.For(_version).TypeInfo<InitializeResponse>()));
                    break;
                case "session/new":
                    BeforeSessionResponse?.Invoke(request);
                    Reply(request, "{\"sessionId\":\"" + (++_sessionNumber == 1 ? "one" : "two") + "\"}");
                    break;
                case "session/close":
                case "session/resume":
                    BeforeSessionResponse?.Invoke(request);
                    Reply(request, "{}");
                    break;
                case "session/prompt":
                    var sessionId = request.Params!.Value.GetProperty("sessionId").GetString()!;
                    if (!_prompts.TryGetValue(sessionId, out var queue)) _prompts[sessionId] = queue = new();
                    queue.Enqueue(request);
                    OnPrompt?.Invoke(request);
                    break;
            }
            return Task.FromResult(true);
        }

        internal void ReplyPrompt(string sessionId, string result)
            => Reply(_prompts[sessionId].Dequeue(), result);

        internal void RejectPrompt(string sessionId, int code)
        {
            var request = _prompts[sessionId].Dequeue();
            Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + request.Id
                + ",\"error\":{\"code\":" + code + ",\"message\":\"prompt rejected\"}}");
        }

        internal void Reply(JsonRpcRequest request, string result)
            => Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"result\":" + result + "}");

        internal void State(string sessionId, string state, string? reason = null)
            => Deliver(StateMessage(sessionId, state, reason));

        internal string StateMessage(string sessionId, string state, string? reason = null)
            => UpdateMessage(sessionId, "{\"sessionUpdate\":\"state_update\",\"state\":\"" + state + "\""
                + (reason is null ? "" : ",\"stopReason\":\"" + reason + "\"") + "}");

        internal void Update(string sessionId, string update) => Deliver(UpdateMessage(sessionId, update));

        private static string UpdateMessage(string sessionId, string update)
            => "{\"jsonrpc\":\"2.0\",\"method\":\"session/update\",\"params\":{\"sessionId\":\"" + sessionId + "\",\"update\":" + update + "}}";

        private void Deliver(string json) => MessageReceived?.Invoke(this, new AcpTransportMessageReceivedEventArgs(json));

        internal Action<string> CaptureDelivery()
        {
            var handlers = MessageReceived;
            return json => handlers?.Invoke(this, new AcpTransportMessageReceivedEventArgs(json));
        }

        internal void DropConnection()
        {
            IsConnected = false;
            ErrorOccurred?.Invoke(this, new AcpTransportErrorEventArgs("connection lost"));
        }

        public void Dispose()
        {
            Client.Dispose();
            IsConnected = false;
        }
    }
}
