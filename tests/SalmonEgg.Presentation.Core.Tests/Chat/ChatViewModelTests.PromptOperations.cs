using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Theory(Timeout = 15000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendPrompt_WhenProviderFailsOrCancels_ReevaluatesPoolAfterTerminalState(bool cancelled)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var service = CreateConnectedChatService();
        service.Setup(chat => chat.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(cancelled ? new OperationCanceledException() : new InvalidOperationException("provider failed"));
        var commands = CreatePromptOperationCommands();
        ViewModelFixture? fixtureReference = null;
        ChatTurnPhase? observedPhase = null;
        commands.Setup(command => command.ReevaluatePoolAsync(It.IsAny<IChatService?>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { observedPhase = (await fixtureReference!.GetStateAsync()).ResolveTurn("conv-1")?.Phase; });
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: commands.Object);
        fixtureReference = fixture;
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        fixture.ViewModel.CurrentPrompt = "prompt";

        // Act
        await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.SendPromptCommand.ExecuteAsync(null));

        // Assert
        Assert.Equal(cancelled ? ChatTurnPhase.Cancelled : ChatTurnPhase.Failed, observedPhase);
        commands.Verify(command => command.ReevaluatePoolAsync(It.IsAny<IChatService?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 15000)]
    public async Task PendingRequestRemoval_ReevaluatesPoolAndShutdownWaitsForCleanup()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var panels = new ChatConversationPanelStateCoordinator(dispatcher);
        var commands = CreatePromptOperationCommands();
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        commands.Setup(command => command.ReevaluatePoolAsync(It.IsAny<IChatService?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref calls) == 1) return Task.CompletedTask;
                cleanupStarted.TrySetResult();
                return release.Task;
            });
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: commands.Object, panelStateCoordinator: panels);
        var request = new AskUserRequestViewModel("request", "remote", "Question", []);
        panels.StoreAskUserRequest("conversation", request);
        await WaitForPromptOperationConditionAsync(dispatcher, () => calls == 1);

        // Act
        panels.RemoveAskUserRequest("conversation", request);
        await WaitForPromptOperationConditionAsync(dispatcher, () => cleanupStarted.Task.IsCompleted);
        var drain = fixture.ViewModel.DrainSessionRuntimeAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.False(drain.IsCompleted);
            release.TrySetResult();
            await AwaitPromptOperationTaskAsync(dispatcher, drain);

            // Assert
            Assert.Equal(2, calls);
            Assert.False(panels.GetSummary("conversation").HasInputRequest);
        }
        finally
        {
            release.TrySetResult();
            await AwaitPromptOperationTaskAsync(dispatcher, drain);
        }
    }

    [Theory(Timeout = 15000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelPrompt_SuccessRetiresOnlyCapturedAskUserRequest(bool replaceBeforeCancellationCompletes)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var panels = new ChatConversationPanelStateCoordinator(dispatcher);
        var commands = CreatePromptOperationCommands();
        var service = CreateConnectedChatService();
        var cancelStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Setup(command => command.CancelPromptAsync(It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<CancellationToken>()))
            .Returns(() => { cancelStarted.TrySetResult(); return release.Task; });
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: commands.Object, panelStateCoordinator: panels);
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        await AwaitPromptOperationTaskAsync(dispatcher, fixture.DispatchAsync(new BeginTurnAction("conv-1", "turn", ChatTurnPhase.Thinking,
            ProfileId: "profile-1", RemoteSessionId: "remote-shared", ConnectionInstanceId: "connection-1")).AsTask());
        var source = new AcpSessionEventSource("profile-1", "connection-1", service.Object);
        var oldRequest = new AskUserRequestViewModel("request", "remote-shared", "Old question", []) { Source = source };
        var replacement = new AskUserRequestViewModel("request", "remote-shared", "New question", []) { Source = source };
        panels.StoreAskUserRequest("conv-1", oldRequest);

        // Act
        var cancel = fixture.ViewModel.CancelPromptCommand.ExecuteAsync(null);
        try
        {
            await WaitForPromptOperationConditionAsync(dispatcher, () => cancelStarted.Task.IsCompleted);
            if (replaceBeforeCancellationCompletes) panels.StoreAskUserRequest("conv-1", replacement);
            release.TrySetResult();
            await AwaitPromptOperationTaskAsync(dispatcher, cancel);

            // Assert
            Assert.Same(replaceBeforeCancellationCompletes ? replacement : null, panels.GetPendingAskUserRequest("conv-1"));
        }
        finally
        {
            release.TrySetResult();
            await AwaitPromptOperationTaskAsync(dispatcher, cancel);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task SendPrompt_TwoConversations_RunIndependentlyAndCancelOnlySelectedConversation()
    {
        var dispatcher = new QueueingSynchronizationContext();
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var calls = new ConcurrentQueue<string>();
        var firstResponse = new TaskCompletionSource<SessionPromptResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<SessionPromptResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CreateConnectedChatService();
        var second = CreateConnectedChatService();
        first.Setup(service => service.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionPromptParams, CancellationToken>((request, token) =>
            {
                calls.Enqueue("first:" + request.SessionId);
                return firstResponse.Task.WaitAsync(token);
            });
        second.Setup(service => service.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionPromptParams, CancellationToken>((request, token) =>
            {
                calls.Enqueue("second:" + request.SessionId);
                return secondResponse.Task.WaitAsync(token);
            });
        second.Setup(service => service.CancelSessionAsync(It.IsAny<SessionCancelParams>()))
            .Callback(() => secondResponse.TrySetResult(new SessionPromptResponse(StopReason.Cancelled) { HasStopReason = true }))
            .Returns(Task.CompletedTask);
        using var firstAdapter = new AcpChatServiceAdapter(first.Object, new AcpEventAdapter(_ => { }, dispatcher));
        using var secondAdapter = new AcpChatServiceAdapter(second.Object, new AcpEventAdapter(_ => { }, dispatcher));
        var commands = CreatePromptOperationCommands();
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: commands.Object, connectionSessionRegistry: registry);
        registry.Upsert(new("profile-1", firstAdapter, new InitializeResponse(), default, "connection-1"));
        registry.Upsert(new("profile-2", secondAdapter, new InitializeResponse(), default, "connection-2"));
        await SelectPromptOperationConversationAsync(fixture, dispatcher, firstAdapter, "conv-1", "profile-1", "connection-1");
        fixture.ViewModel.CurrentPrompt = "first prompt";
        var firstSend = fixture.ViewModel.SendPromptCommand.ExecuteAsync(null);
        try
        {
            await WaitForPromptOperationConditionAsync(dispatcher, () => calls.Count == 1);
            await SelectPromptOperationConversationAsync(fixture, dispatcher, secondAdapter, "conv-2", "profile-2", "connection-2");
            fixture.ViewModel.CurrentPrompt = "second prompt";
            var secondSend = fixture.ViewModel.SendPromptCommand.ExecuteAsync(null);
            await WaitForPromptOperationConditionAsync(dispatcher, () => calls.Count == 2);

            Assert.False(firstSend.IsCompleted);
            Assert.False(secondSend.IsCompleted);
            Assert.Equal(new[] { "first:remote-shared", "second:remote-shared" }, calls);
            fixture.ViewModel.CurrentPrompt = "duplicate";
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.SendPromptCommand.ExecuteAsync(null));
            Assert.Equal(2, calls.Count);
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.CancelPromptCommand.ExecuteAsync(null));
            await AwaitPromptOperationTaskAsync(dispatcher, secondSend);

            Assert.False(firstSend.IsCompleted);
            Assert.Equal(ChatTurnPhase.WaitingForAgent, (await fixture.GetStateAsync()).ResolveTurn("conv-1")!.Phase);
            Assert.Equal(ChatTurnPhase.Cancelled, (await fixture.GetStateAsync()).ResolveTurn("conv-2")!.Phase);
            first.Verify(service => service.CancelSessionAsync(It.IsAny<SessionCancelParams>()), Times.Never);
            second.Verify(service => service.CancelSessionAsync(It.Is<SessionCancelParams>(request => request.SessionId == "remote-shared")), Times.Once);
            firstResponse.TrySetResult(new SessionPromptResponse(StopReason.EndTurn) { HasStopReason = true });
            await AwaitPromptOperationTaskAsync(dispatcher, firstSend);
            Assert.Equal(ChatTurnPhase.Completed, (await fixture.GetStateAsync()).ResolveTurn("conv-1")!.Phase);
            Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
        }
        finally
        {
            firstResponse.TrySetCanceled(TestContext.Current.CancellationToken);
            secondResponse.TrySetCanceled(TestContext.Current.CancellationToken);
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.DrainPromptOperationsAsync(TestContext.Current.CancellationToken));
        }
    }

    [Theory(Timeout = 15000)]
    [InlineData(1, false)]
    [InlineData(32, true)]
    public async Task SendPrompt_ResponseBeforeBufferedChunksReachViewModel_PreservesBackgroundUnread(
        int chunkCount, bool anotherSessionIsHydrating)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var adapterDispatcher = new QueueingSynchronizationContext();
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Inline continuations establish completion ingress before either dispatcher is pumped.
        var response = new TaskCompletionSource<SessionPromptResponse>();
        var first = CreateConnectedChatService();
        var second = CreateConnectedChatService();
        first.Setup(service => service.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Returns(() => { entered.TrySetResult(); return response.Task; });
        using var firstAdapter = new AcpChatServiceAdapter(first.Object, new AcpEventAdapter(_ => { }, adapterDispatcher));
        using var secondAdapter = new AcpChatServiceAdapter(second.Object, new AcpEventAdapter(_ => { }, dispatcher));
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: CreatePromptOperationCommands().Object,
            connectionSessionRegistry: registry);
        registry.Upsert(new("profile-1", firstAdapter, new InitializeResponse(), default, "connection-1"));
        registry.Upsert(new("profile-2", secondAdapter, new InitializeResponse(), default, "connection-2"));
        await SelectPromptOperationConversationAsync(fixture, dispatcher, firstAdapter, "conv-1", "profile-1", "connection-1");
        fixture.ViewModel.CurrentPrompt = "background prompt";
        var send = fixture.ViewModel.SendPromptCommand.ExecuteAsync(null);
        try
        {
            await WaitForPromptOperationConditionAsync(dispatcher, () => entered.Task.IsCompleted);
            await SelectPromptOperationConversationAsync(fixture, dispatcher, secondAdapter, "conv-2", "profile-2", "connection-2");
            fixture.ViewModel.CurrentPrompt = "foreground draft";
            firstAdapter.ReleaseUnscopedBufferedUpdates();
            if (anotherSessionIsHydrating) firstAdapter.BeginHydrationBufferingScope("another-remote");

            // Act: ACP delivers every chunk before its response; only native UI dispatch is delayed.
            for (var index = 0; index < chunkCount; index++)
            {
                first.Raise(service => service.SessionUpdateReceived += null,
                    new SessionUpdateEventArgs("remote-shared", new AgentMessageUpdate(new TextContentBlock("reply"))));
            }
            response.SetResult(new SessionPromptResponse(StopReason.EndTurn) { HasStopReason = true });
            await dispatcher.RunUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            adapterDispatcher.RunAll();
            var completed = Task.WhenAll(send, WaitForPendingSessionUpdatesAsync(fixture.ViewModel));
            await WaitForPromptOperationConditionAsync(dispatcher, () =>
            {
                adapterDispatcher.RunAll();
                return completed.IsCompleted;
            });
            await completed;

            // Assert
            var state = await fixture.GetStateAsync();
            var turn = state.ResolveTurn("conv-1");
            Assert.Equal(ChatTurnPhase.Completed, turn?.Phase);
            Assert.Equal(string.Concat(Enumerable.Repeat("reply", chunkCount)),
                string.Concat(state.ResolveContentSlice("conv-1")!.Value.Transcript.Where(message => !message.IsOutgoing).Select(message => message.TextContent)));
            var attention = (await fixture.GetAttentionStateAsync()).Conversations.GetValueOrDefault("conv-1");
            Assert.NotNull(attention);
            Assert.True(attention.HasUnread);
            Assert.Same(state.ResolveContentSlice("conv-1")!.Value.Transcript.Last(), attention.Content);
            Assert.Equal(ConversationStatusGroup.NeedsAttention, ConversationStatusPolicy.Resolve(turn, default, attention).Group);
            Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
            Assert.Equal("foreground draft", fixture.ViewModel.CurrentPrompt);
        }
        finally
        {
            firstAdapter.SuppressAllBufferedUpdates("TestCleanup");
            adapterDispatcher.RunAll();
            response.TrySetCanceled(TestContext.Current.CancellationToken);
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.DrainPromptOperationsAsync(TestContext.Current.CancellationToken));
        }
    }

    [Theory(Timeout = 15000)]
    [InlineData(AcpConnectionRetirementReason.TransportLost, ChatTurnPhase.Failed)]
    [InlineData(AcpConnectionRetirementReason.Replaced, ChatTurnPhase.Failed)]
    [InlineData(AcpConnectionRetirementReason.Disconnected, ChatTurnPhase.Cancelled)]
    [InlineData(AcpConnectionRetirementReason.Shutdown, ChatTurnPhase.Cancelled)]
    public async Task SendPrompt_BackgroundConnectionRetires_CommitsTerminalReasonBeforeCancellingOperation(
        AcpConnectionRetirementReason reason, ChatTurnPhase expectedPhase)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var phaseAtCancellation = new TaskCompletionSource<ChatTurnPhase?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<SessionPromptResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CreateConnectedChatService();
        var second = CreateConnectedChatService();
        ViewModelFixture? fixtureReference = null;
        first.Setup(service => service.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionPromptParams, CancellationToken>(async (_, token) =>
            {
                using var registration = token.Register(() =>
                {
                    phaseAtCancellation.TrySetResult(fixtureReference!.ChatStore.ReadCommittedState()?.ResolveTurn("conv-1")?.Phase);
                    response.TrySetCanceled(token);
                });
                entered.TrySetResult();
                return await response.Task.ConfigureAwait(false);
            });
        using var firstAdapter = new AcpChatServiceAdapter(first.Object, new AcpEventAdapter(_ => { }, dispatcher));
        using var secondAdapter = new AcpChatServiceAdapter(second.Object, new AcpEventAdapter(_ => { }, dispatcher));
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: CreatePromptOperationCommands().Object,
            connectionSessionRegistry: registry);
        fixtureReference = fixture;
        registry.Upsert(new("profile-1", firstAdapter, new InitializeResponse(), default, "connection-1"));
        registry.Upsert(new("profile-2", secondAdapter, new InitializeResponse(), default, "connection-2"));
        await SelectPromptOperationConversationAsync(fixture, dispatcher, firstAdapter, "conv-1", "profile-1", "connection-1");
        fixture.ViewModel.CurrentPrompt = "background prompt";
        var send = fixture.ViewModel.SendPromptCommand.ExecuteAsync(null);
        try
        {
            await WaitForPromptOperationConditionAsync(dispatcher, () => entered.Task.IsCompleted);
            await SelectPromptOperationConversationAsync(fixture, dispatcher, secondAdapter, "conv-2", "profile-2", "connection-2");
            fixture.ViewModel.CurrentPrompt = "foreground draft";

            // Act
            registry.RemoveByProfile("profile-1", reason);
            await AwaitPromptOperationTaskAsync(dispatcher, send);
            await AwaitPromptOperationTaskAsync(dispatcher, WaitForPendingSessionUpdatesAsync(fixture.ViewModel));

            // Assert: the synchronous token callback makes the cancellation race deterministic.
            Assert.Equal(expectedPhase, await phaseAtCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var state = await fixture.GetStateAsync();
            var turn = state.ResolveTurn("conv-1");
            Assert.Equal(expectedPhase, turn?.Phase);
            Assert.Equal(expectedPhase == ChatTurnPhase.Failed ? ConversationStatusGroup.NeedsAttention : ConversationStatusGroup.Other,
                ConversationStatusPolicy.Resolve(turn, default, null).Group);
            Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
            Assert.Equal("foreground draft", fixture.ViewModel.CurrentPrompt);
            Assert.Null(state.ActiveTurn);
            Assert.Same(secondAdapter, fixture.ViewModel.CurrentChatService);
        }
        finally
        {
            response.TrySetCanceled(TestContext.Current.CancellationToken);
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.DrainPromptOperationsAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact(Timeout = 15000)]
    public async Task SendPrompt_BackgroundFailure_PreservesForegroundDraft()
    {
        var dispatcher = new QueueingSynchronizationContext();
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<SessionPromptResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateConnectedChatService();
        service.Setup(chat => chat.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionPromptParams, CancellationToken>((_, token) =>
            {
                entered.TrySetResult();
                return response.Task.WaitAsync(token);
            });
        using var adapter = new AcpChatServiceAdapter(service.Object, new AcpEventAdapter(_ => { }, dispatcher));
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: CreatePromptOperationCommands().Object,
            connectionSessionRegistry: registry);
        registry.Upsert(new("profile-1", adapter, new InitializeResponse(), default, "connection-1"));
        await SelectPromptOperationConversationAsync(fixture, dispatcher, adapter, "conv-1", "profile-1", "connection-1");
        fixture.ViewModel.CurrentPrompt = "old prompt";
        var send = fixture.ViewModel.SendPromptCommand.ExecuteAsync(null);
        try
        {
            await WaitForPromptOperationConditionAsync(dispatcher, () => entered.Task.IsCompleted);
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.DispatchAsync(new SelectConversationAction("conv-2")).AsTask());
            await dispatcher.RunUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            fixture.ViewModel.CurrentPrompt = "new draft";

            response.TrySetException(new InvalidOperationException("provider failed"));
            await AwaitPromptOperationTaskAsync(dispatcher, send);

            Assert.Equal("new draft", fixture.ViewModel.CurrentPrompt);
            Assert.Equal(ChatTurnPhase.Failed, (await fixture.GetStateAsync()).ResolveTurn("conv-1")!.Phase);
            Assert.Equal("provider failed", (await fixture.GetStateAsync()).ResolveTurn("conv-1")!.FailureMessage);
            Assert.Null((await fixture.GetStateAsync()).ActiveTurn);
        }
        finally
        {
            response.TrySetCanceled(TestContext.Current.CancellationToken);
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.DrainPromptOperationsAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact(Timeout = 15000)]
    public async Task SendPrompt_FailureAfterUserEditsThenClearsDraft_DoesNotRestoreOldPrompt()
    {
        var dispatcher = new QueueingSynchronizationContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<SessionPromptResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateConnectedChatService();
        service.Setup(chat => chat.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionPromptParams, CancellationToken>((_, token) =>
            {
                entered.TrySetResult();
                return response.Task.WaitAsync(token);
            });
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: CreatePromptOperationCommands().Object);
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        fixture.ViewModel.CurrentPrompt = "old prompt";
        var send = fixture.ViewModel.SendPromptCommand.ExecuteAsync(null);
        try
        {
            await WaitForPromptOperationConditionAsync(dispatcher, () => entered.Task.IsCompleted);
            fixture.ViewModel.CurrentPrompt = "replacement";
            await dispatcher.RunUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            fixture.ViewModel.CurrentPrompt = string.Empty;
            await dispatcher.RunUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            response.TrySetException(new InvalidOperationException("provider failed"));
            await AwaitPromptOperationTaskAsync(dispatcher, send);

            Assert.Equal(string.Empty, fixture.ViewModel.CurrentPrompt);
            Assert.Equal(ChatTurnPhase.Failed, (await fixture.GetStateAsync()).ResolveTurn("conv-1")!.Phase);
        }
        finally
        {
            response.TrySetCanceled(TestContext.Current.CancellationToken);
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.DrainPromptOperationsAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact(Timeout = 15000)]
    public async Task DrainPromptOperations_CancelsAndObservesOwnedSendTasks()
    {
        var dispatcher = new QueueingSynchronizationContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateConnectedChatService();
        service.Setup(chat => chat.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionPromptParams, CancellationToken>(async (_, token) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { cancelled.TrySetResult(); }
                return new SessionPromptResponse(StopReason.Cancelled) { HasStopReason = true };
            });
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: CreatePromptOperationCommands().Object);
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        fixture.ViewModel.CurrentPrompt = "prompt";
        var send = fixture.ViewModel.SendPromptCommand.ExecuteAsync(null);
        try
        {
            await WaitForPromptOperationConditionAsync(dispatcher, () => entered.Task.IsCompleted);
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.DrainPromptOperationsAsync(TestContext.Current.CancellationToken));
            await AwaitPromptOperationTaskAsync(dispatcher, send);

            Assert.True(cancelled.Task.IsCompleted);
            Assert.Equal(ChatTurnPhase.Cancelled, (await fixture.GetStateAsync()).ResolveTurn("conv-1")!.Phase);
        }
        finally
        {
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.DrainPromptOperationsAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact(Timeout = 15000)]
    public async Task SendPrompt_ExpiredRemoteSession_RetriesWithinOriginalTurn()
    {
        var dispatcher = new QueueingSynchronizationContext();
        var service = CreateConnectedChatService();
        service.Setup(chat => chat.SendPromptAsync(It.Is<SessionPromptParams>(request => request.SessionId == "remote-shared"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AcpException(JsonRpcErrorCode.SessionNotFound, "Session not found"));
        service.Setup(chat => chat.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-recovered"));
        service.Setup(chat => chat.SendPromptAsync(It.Is<SessionPromptParams>(request => request.SessionId == "remote-recovered"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionPromptResponse(StopReason.EndTurn) { HasStopReason = true });
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: CreatePromptOperationCommands().Object);
        fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Agent", Transport = TransportType.Stdio, StdioCommand = "agent" });
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        fixture.ViewModel.CurrentPrompt = "recover prompt";

        await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.SendPromptCommand.ExecuteAsync(null));

        var state = await fixture.GetStateAsync();
        Assert.True(state.ResolveTurn("conv-1")?.FailureMessage is null,
            state.ResolveTurn("conv-1")?.FailureMessage);
        Assert.Equal(ChatTurnPhase.Completed, state.ResolveTurn("conv-1")!.Phase);
        Assert.Equal("remote-recovered", state.ResolveTurn("conv-1")!.RemoteSessionId);
        Assert.Equal("remote-recovered", state.ResolveBinding("conv-1")!.RemoteSessionId);
        Assert.Single(fixture.ChatStore.Actions.OfType<BeginTurnAction>());
        service.Verify(chat => chat.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact(Timeout = 15000)]
    public async Task SendPrompt_ResponseWithoutStopReason_EndsAsProtocolFailure()
    {
        var dispatcher = new QueueingSynchronizationContext();
        var service = CreateConnectedChatService();
        service.Setup(chat => chat.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize("{}", AcpJsonContext.Default.SessionPromptResponse)!);
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: CreatePromptOperationCommands().Object);
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        fixture.ViewModel.CurrentPrompt = "prompt";

        await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.SendPromptCommand.ExecuteAsync(null));

        var turn = (await fixture.GetStateAsync()).ResolveTurn("conv-1");
        Assert.NotNull(turn);
        Assert.Equal(ChatTurnPhase.Failed, turn.Phase);
        Assert.False(turn.HasStopReason);
        Assert.False(string.IsNullOrWhiteSpace(turn.FailureMessage));
    }

    private static Mock<IAcpConnectionCommands> CreatePromptOperationCommands()
    {
        var orchestrator = new AcpSessionCommandOrchestrator(NullLogger<AcpSessionCommandOrchestrator>.Instance, new StaticMcpResolver([]));
        var commands = new Mock<IAcpConnectionCommands>();
        commands.Setup(command => command.EnsureRemoteSessionAsync(It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<IAcpChatCoordinatorSink, Func<CancellationToken, Task<bool>>, CancellationToken>((sink, auth, token) =>
                orchestrator.EnsureRemoteSessionAsync(sink, auth, static () => { }, token));
        commands.Setup(command => command.DispatchPromptToRemoteSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, string?, IAcpChatCoordinatorSink, Func<CancellationToken, Task<bool>>, CancellationToken>((remote, text, id, sink, auth, token) =>
                orchestrator.DispatchPromptToRemoteSessionAsync(remote, text, id, sink, auth, orchestrator.EnsureRemoteSessionAsync, token));
        commands.Setup(command => command.CancelPromptAsync(It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<CancellationToken>()))
            .Returns<IAcpChatCoordinatorSink, CancellationToken>(orchestrator.CancelPromptAsync);
        return commands;
    }

    private static async Task SelectPromptOperationConversationAsync(ViewModelFixture fixture,
        QueueingSynchronizationContext dispatcher, IChatService service, string conversationId, string profileId, string connectionId)
    {
        await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.ReplaceChatServiceAsync(service, TestContext.Current.CancellationToken));
        await fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction(profileId)).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction(connectionId)).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected)).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await AwaitPromptOperationTaskAsync(dispatcher, fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = conversationId,
            Bindings = (state.Bindings ?? ImmutableDictionary<string, ConversationBindingSlice>.Empty)
                .SetItem(conversationId, new(conversationId, "remote-shared", profileId))
        }).AsTask());
        await dispatcher.RunUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitForQueueingConversationReadyAsync(dispatcher, fixture, conversationId)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static Task WaitForPromptOperationConditionAsync(QueueingSynchronizationContext dispatcher, Func<bool> condition)
        => WaitForConditionAsync(() =>
        {
            dispatcher.RunAll();
            return Task.FromResult(condition());
        }, timeoutMilliseconds: 5000).WaitAsync(TestContext.Current.CancellationToken);

    private static Task AwaitPromptOperationTaskAsync(QueueingSynchronizationContext dispatcher, Task task)
        => dispatcher.RunUntilCompletedAsync(task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
}
