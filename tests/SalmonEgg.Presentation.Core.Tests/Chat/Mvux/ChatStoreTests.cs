using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Mvux;

[Collection("NonParallel")]
public class ChatStoreTests
{
    [Fact]
    public async Task ReadCommittedState_AfterBindingUpdate_ReturnsTheSameAuthoritativeSnapshot()
    {
        // Arrange
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var store = new ChatStore(state);
        Assert.Null(store.ReadCommittedState());

        // Act
        await store.Dispatch(new SetBindingSliceAction(new("conversation", "remote", "profile")));

        // Assert
        var asynchronous = await store.GetCurrentStateAsync();
        Assert.Same(asynchronous, store.ReadCommittedState());
        Assert.Equal("profile", store.ReadCommittedState()!.ResolveBinding("conversation")?.ProfileId);
    }

    [Fact]
    public async Task GivenStore_WhenDispatchAction_ThenStateIsUpdatedViaReducer()
    {
        // Arrange
        var initialState = new ChatState(HydratedConversationId: "initial");
        await using var state = State.Value(new object(), () => initialState);
        var store = new ChatStore(state);
        var newConversationId = "updated-id";
        var action = new SelectConversationAction(newConversationId);

        // Act
        await store.Dispatch(action);

        // Assert
        var currentState = await WaitForStateAsync(state, current => string.Equals(current?.HydratedConversationId, newConversationId, System.StringComparison.Ordinal));
        Assert.NotNull(currentState);
        Assert.Equal(newConversationId, currentState.HydratedConversationId);
    }

    [Fact]
    public async Task GivenStore_WhenMultipleDispatches_ThenStateTransitionsSequentially()
    {
        // Arrange
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var store = new ChatStore(state);

        // Act
        await store.Dispatch(new BeginTurnAction("initial", "turn-1", ChatTurnPhase.WaitingForAgent));
        await store.Dispatch(new SelectConversationAction("conv-1"));

        // Assert
        var currentState = await WaitForStateAsync(
            state,
            current => current is not null
                && string.Equals(current.HydratedConversationId, "conv-1", System.StringComparison.Ordinal)
                && current.ActiveTurn is null);
        Assert.NotNull(currentState);
        Assert.Null(currentState.ActiveTurn);
        Assert.Equal("conv-1", currentState.HydratedConversationId);
        Assert.Equal("turn-1", currentState.ResolveTurn("initial")!.TurnId);
    }

    [Fact]
    public async Task TurnOnlyMutations_PublishReactiveStateWithoutSchedulingWorkspaceWrites()
    {
        // Arrange
        await using var state = State.Value(new object(), () => ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Generation = 7
        });
        var writer = new FakeWorkspaceWriter();
        var store = new ChatStore(state, writer);
        var observedPhases = new Dictionary<ChatTurnPhase, TaskCompletionSource<ChatState>>
        {
            [ChatTurnPhase.WaitingForAgent] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            [ChatTurnPhase.Responding] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            [ChatTurnPhase.Completed] = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        state.ForEach((current, _) =>
        {
            if (current?.ActiveTurn is { } turn && observedPhases.TryGetValue(turn.Phase, out var observed))
            {
                observed.TrySetResult(current);
            }

            return ValueTask.CompletedTask;
        }, out var subscription);
        using var stateSubscription = subscription;

        // Act
        await store.Dispatch(new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent));
        var running = await observedPhases[ChatTurnPhase.WaitingForAgent].Task.WaitAsync(TimeSpan.FromSeconds(5));
        await store.Dispatch(new SetTurnBindingAction("conv-1", "turn-1", "profile-1", "remote-1", "connection-1"));
        await store.Dispatch(new AdvanceTurnPhaseAction("conv-1", "turn-1", ChatTurnPhase.Responding, ConnectionInstanceId: "connection-1"));
        var responding = await observedPhases[ChatTurnPhase.Responding].Task.WaitAsync(TimeSpan.FromSeconds(5));
        await store.Dispatch(new CompleteTurnAction("conv-1", "turn-1", "end_turn", true, "connection-1"));
        var completed = await observedPhases[ChatTurnPhase.Completed].Task.WaitAsync(TimeSpan.FromSeconds(5));
        await store.Dispatch(new ClearTerminalTurnAction("conv-1"));

        // Assert
        Assert.Equal(7, running.Generation);
        Assert.Equal(7, responding.Generation);
        Assert.Equal(7, completed.Generation);
        Assert.Equal("connection-1", responding.ActiveTurn!.ConnectionInstanceId);
        Assert.Equal("end_turn", completed.ActiveTurn!.StopReason);
        Assert.True(completed.ActiveTurn.HasStopReason);
        Assert.Null((await store.GetCurrentStateAsync()).ActiveTurn);
        Assert.Equal(0, writer.EnqueueCount);
    }

    [Fact]
    public async Task GivenStore_WhenGenerationIncreases_ThenWorkspaceWriterProjectsSnapshots()
    {
        // Arrange
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var writer = new FakeWorkspaceWriter();
        var store = new ChatStore(state, writer);

        // Act
        await store.Dispatch(new SetDraftTextAction("hello"));
        await store.Dispatch(new SetDraftTextAction("world"));

        // Assert
        Assert.Equal(2, writer.EnqueueCount);
        Assert.Equal(new long[] { 1L, 2L }, writer.EnqueuedGenerations);
        Assert.All(writer.ScheduleSaveFlags, Assert.True);
    }

    [Fact]
    public async Task GivenStore_WhenBackgroundConversationSliceChanges_ThenWorkspaceWriterTracksNewGenerationWithoutChangingActiveProjection()
    {
        // Arrange
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var writer = new FakeWorkspaceWriter();
        var store = new ChatStore(state, writer);

        await store.Dispatch(new SelectConversationAction("conv-1"));
        var message = new ConversationMessageSnapshot
        {
            Id = "m-1",
            ContentType = "text",
            TextContent = "stale"
        };

        // Act
        await store.Dispatch(new UpsertTranscriptMessageAction("conv-2", message));

        // Assert
        Assert.Equal(2, writer.EnqueueCount);
        Assert.Equal(new long[] { 1L, 2L }, writer.EnqueuedGenerations);
    }

    private sealed class FakeWorkspaceWriter : IWorkspaceWriter
    {
        public List<long> EnqueuedGenerations { get; } = new();

        public List<bool> ScheduleSaveFlags { get; } = new();

        public int EnqueueCount => EnqueuedGenerations.Count;

        public void Enqueue(ChatState state, bool scheduleSave)
        {
            EnqueuedGenerations.Add(state.Generation);
            ScheduleSaveFlags.Add(scheduleSave);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static async Task<ChatState> WaitForStateAsync(IState<ChatState> state, System.Func<ChatState, bool> predicate, int maxAttempts = 20, int delayMs = 10)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var current = await state ?? ChatState.Empty;
            if (predicate(current))
            {
                return current;
            }

            await Task.Delay(delayMs);
        }

        return await state ?? ChatState.Empty;
    }
}
