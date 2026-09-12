using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task ReadReceipt_OldSnapshotAfterStreamingAppend_DoesNotClearLatestReply()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        var service = CreateConnectedChatService();
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        await fixture.DispatchAsync(new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent,
            ProfileId: "profile-1", RemoteSessionId: "remote-shared", ConnectionInstanceId: "connection-1"));
        var first = await EmitUnreadReplyAsync(fixture, dispatcher, service, "first");
        var second = await EmitUnreadReplyAsync(fixture, dispatcher, service, " second");

        var oldReceipt = fixture.ViewModel.AcknowledgeVisibleReplyAsync("conv-1", first);
        await AwaitWithSynchronizationContextAsync(dispatcher, oldReceipt);
        Assert.False(await oldReceipt);
        Assert.True(HasUnreadAttention(await fixture.GetAttentionStateAsync(), "conv-1"));
        var currentReceipt = fixture.ViewModel.AcknowledgeVisibleReplyAsync("conv-1", second);
        await AwaitWithSynchronizationContextAsync(dispatcher, currentReceipt);

        Assert.True(await currentReceipt);
        Assert.False(HasUnreadAttention(await fixture.GetAttentionStateAsync(), "conv-1"));
    }

    [Fact]
    public async Task ReadReceipt_ViewBecomesHiddenBeforeUiQueueRuns_DoesNotClearUnread()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        var service = CreateConnectedChatService();
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        await fixture.DispatchAsync(new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent,
            ProfileId: "profile-1", RemoteSessionId: "remote-shared", ConnectionInstanceId: "connection-1"));
        var reply = await EmitUnreadReplyAsync(fixture, dispatcher, service, "reply");
        var visible = true;

        var receipt = fixture.ViewModel.AcknowledgeVisibleReplyAsync("conv-1", reply, () => visible);
        visible = false;
        await AwaitWithSynchronizationContextAsync(dispatcher, receipt);

        Assert.False(await receipt);
        Assert.True(HasUnreadAttention(await fixture.GetAttentionStateAsync(), "conv-1"));
    }

    [Fact]
    public async Task ReadReceipt_ConversationSwitchesBeforeUiQueueRuns_DoesNotClearBackgroundReply()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        var service = CreateConnectedChatService();
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        await fixture.DispatchAsync(new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent,
            ProfileId: "profile-1", RemoteSessionId: "remote-shared", ConnectionInstanceId: "connection-1"));
        var reply = await EmitUnreadReplyAsync(fixture, dispatcher, service, "reply");

        var receipt = fixture.ViewModel.AcknowledgeVisibleReplyAsync("conv-1", reply);
        await fixture.ChatStore.Dispatch(new SelectConversationAction("conv-2"));
        await AwaitWithSynchronizationContextAsync(dispatcher, receipt);

        Assert.False(await receipt);
        Assert.True(HasUnreadAttention(await fixture.GetAttentionStateAsync(), "conv-1"));
    }

    [Fact]
    public async Task ReadReceipt_BindingChangesBeforeReceipt_DoesNotClearUnread()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        var service = CreateConnectedChatService();
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        await fixture.DispatchAsync(new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent,
            ProfileId: "profile-1", RemoteSessionId: "remote-shared", ConnectionInstanceId: "connection-1"));
        var reply = await EmitUnreadReplyAsync(fixture, dispatcher, service, "reply");
        await fixture.DispatchAsync(new SetBindingSliceAction(new("conv-1", "replacement-remote", "profile-1")));

        var receipt = fixture.ViewModel.AcknowledgeVisibleReplyAsync("conv-1", reply);
        await AwaitWithSynchronizationContextAsync(dispatcher, receipt);

        Assert.False(await receipt);
        Assert.True(HasUnreadAttention(await fixture.GetAttentionStateAsync(), "conv-1"));
    }

    [Fact(Timeout = 15000)]
    public async Task ReadReceipt_TerminalResult_WaitsForCurrentVisibleReplyObservation()
    {
        var dispatcher = new QueueingSynchronizationContext();
        var response = new TaskCompletionSource<SessionPromptResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateConnectedChatService();
        service.Setup(chat => chat.SendPromptAsync(Moq.It.IsAny<SessionPromptParams>(), Moq.It.IsAny<CancellationToken>()))
            .Returns<SessionPromptParams, CancellationToken>((_, token) => response.Task.WaitAsync(token));
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: CreatePromptOperationCommands().Object);
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        fixture.ViewModel.CurrentPrompt = "prompt";
        var send = fixture.ViewModel.SendPromptCommand.ExecuteAsync(null);
        await WaitForPromptOperationConditionAsync(dispatcher,
            () => fixture.ChatStore.LatestState.ResolveTurn("conv-1")?.Phase == ChatTurnPhase.WaitingForAgent);
        var reply = await EmitUnreadReplyAsync(fixture, dispatcher, service, "reply");
        var observationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseObservation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task> observe = async () =>
        {
            observationEntered.TrySetResult();
            await releaseObservation.Task;
            await fixture.ViewModel.AcknowledgeVisibleReplyAsync("conv-1", reply);
        };
        fixture.ViewModel.ReplyReadObservationRequested += observe;

        try
        {
            response.TrySetResult(new SessionPromptResponse(StopReason.EndTurn) { HasStopReason = true });
            await WaitForPromptOperationConditionAsync(dispatcher, () => observationEntered.Task.IsCompleted);
            Assert.NotEqual(ChatTurnPhase.Completed, fixture.ChatStore.LatestState.ResolveTurn("conv-1")!.Phase);
            Assert.True(HasUnreadAttention(await fixture.GetAttentionStateAsync(), "conv-1"));
            releaseObservation.TrySetResult();
            await AwaitPromptOperationTaskAsync(dispatcher, send);

            Assert.Equal(ChatTurnPhase.Completed, fixture.ChatStore.LatestState.ResolveTurn("conv-1")!.Phase);
            Assert.False(HasUnreadAttention(await fixture.GetAttentionStateAsync(), "conv-1"));
        }
        finally
        {
            releaseObservation.TrySetResult();
            response.TrySetCanceled(TestContext.Current.CancellationToken);
            fixture.ViewModel.ReplyReadObservationRequested -= observe;
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.DrainPromptOperationsAsync(TestContext.Current.CancellationToken));
        }
    }

    [Theory(Timeout = 15000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadReceipt_VisibilityObserverFails_PreservesProtocolCompletionAndUnread(bool throwsSynchronously)
    {
        var dispatcher = new QueueingSynchronizationContext();
        var response = new TaskCompletionSource<SessionPromptResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateConnectedChatService();
        service.Setup(chat => chat.SendPromptAsync(Moq.It.IsAny<SessionPromptParams>(), Moq.It.IsAny<CancellationToken>()))
            .Returns<SessionPromptParams, CancellationToken>((_, token) => response.Task.WaitAsync(token));
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: CreatePromptOperationCommands().Object);
        await SelectPromptOperationConversationAsync(fixture, dispatcher, service.Object, "conv-1", "profile-1", "connection-1");
        fixture.ViewModel.CurrentPrompt = "prompt";
        var send = fixture.ViewModel.SendPromptCommand.ExecuteAsync(null);
        Func<Task>? failedObservation = null;
        Func<Task>? remainingObservation = null;
        try
        {
            await WaitForPromptOperationConditionAsync(dispatcher,
                () => fixture.ChatStore.LatestState.ResolveTurn("conv-1")?.Phase == ChatTurnPhase.WaitingForAgent);
            var reply = await EmitUnreadReplyAsync(fixture, dispatcher, service, "reply");
            var unread = (await fixture.GetAttentionStateAsync()).Conversations["conv-1"];
            var observed = 0;
            failedObservation = () =>
            {
                var error = new InvalidOperationException("Native visibility observation failed.");
                if (throwsSynchronously) throw error;
                return Task.FromException(error);
            };
            remainingObservation = () =>
            {
                observed++;
                return Task.CompletedTask;
            };
            fixture.ViewModel.ReplyReadObservationRequested += failedObservation;
            fixture.ViewModel.ReplyReadObservationRequested += remainingObservation;

            response.TrySetResult(new SessionPromptResponse(StopReason.EndTurn) { HasStopReason = true });
            await AwaitPromptOperationTaskAsync(dispatcher, send);

            var turn = fixture.ChatStore.LatestState.ResolveTurn("conv-1");
            Assert.NotNull(turn);
            Assert.Equal(ChatTurnPhase.Completed, turn.Phase);
            Assert.Equal(StopReason.EndTurn.Value, turn.StopReason);
            Assert.True(turn.HasStopReason);
            Assert.Null(turn.FailureMessage);
            Assert.Equal(1, observed);
            var remainingUnread = (await fixture.GetAttentionStateAsync()).Conversations["conv-1"];
            Assert.True(remainingUnread.HasUnread);
            Assert.Equal(unread.UnreadVersion, remainingUnread.UnreadVersion);
            Assert.Same(reply, remainingUnread.Content);
        }
        finally
        {
            if (failedObservation is not null) fixture.ViewModel.ReplyReadObservationRequested -= failedObservation;
            if (remainingObservation is not null) fixture.ViewModel.ReplyReadObservationRequested -= remainingObservation;
            response.TrySetCanceled(TestContext.Current.CancellationToken);
            await AwaitPromptOperationTaskAsync(dispatcher, fixture.ViewModel.DrainPromptOperationsAsync(TestContext.Current.CancellationToken));
        }
    }

    private static async Task<ConversationMessageSnapshot> EmitUnreadReplyAsync(
        ViewModelFixture fixture,
        QueueingSynchronizationContext dispatcher,
        Moq.Mock<IChatService> service,
        string text)
    {
        var previous = await fixture.GetAttentionStateAsync();
        var version = previous.TryGetConversation("conv-1", out var current) ? current!.UnreadVersion : 0;
        service.Raise(chat => chat.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-shared", new AgentMessageUpdate(new TextContentBlock(text))));
        await WaitForConditionAsync(async () =>
        {
            dispatcher.RunAll();
            var state = await fixture.GetAttentionStateAsync();
            return state.TryGetConversation("conv-1", out var attention) && attention!.UnreadVersion > version;
        });
        await dispatcher.RunUntilIdleAsync();
        return (await fixture.GetAttentionStateAsync()).Conversations["conv-1"].Content!;
    }
}
