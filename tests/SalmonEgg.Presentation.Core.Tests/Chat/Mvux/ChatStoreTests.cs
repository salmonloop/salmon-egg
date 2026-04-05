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
        await store.Dispatch(new SetPromptInFlightAction(true));
        await store.Dispatch(new SelectConversationAction("conv-1"));

        // Assert
        var currentState = await WaitForStateAsync(
            state,
            current => current is not null
                && string.Equals(current.HydratedConversationId, "conv-1", System.StringComparison.Ordinal)
                && current.IsPromptInFlight == false);
        Assert.NotNull(currentState);
        Assert.False(currentState.IsPromptInFlight);
        Assert.Equal("conv-1", currentState.HydratedConversationId);
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
