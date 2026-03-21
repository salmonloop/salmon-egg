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
        await using var state = State.Value(this, () => initialState);
        var store = new ChatStore(state);
        var newConversationId = "updated-id";
        var action = new SelectConversationAction(newConversationId);

        // Act
        await store.Dispatch(action);

        // Assert
        var currentState = await state;
        Assert.NotNull(currentState);
        Assert.Null(currentState.SelectedConversationId);
        Assert.Equal(newConversationId, currentState.HydratedConversationId);
    }

    [Fact]
    public async Task GivenStore_WhenMultipleDispatches_ThenStateTransitionsSequentially()
    {
        // Arrange
        await using var state = State.Value(this, () => ChatState.Empty);
        var store = new ChatStore(state);

        // Act
        await store.Dispatch(new SetPromptInFlightAction(true));
        await store.Dispatch(new SelectConversationAction("conv-1"));

        // Assert
        var currentState = await state;
        Assert.NotNull(currentState);
        Assert.False(currentState.IsPromptInFlight);
        Assert.Null(currentState.SelectedConversationId);
        Assert.Equal("conv-1", currentState.HydratedConversationId);
    }

    [Fact]
    public async Task GivenStore_WhenGenerationIncreases_ThenWorkspaceWriterProjectsSnapshots()
    {
        // Arrange
        await using var state = State.Value(this, () => ChatState.Empty);
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
    public async Task GivenStore_WhenDispatchDoesNotChangeState_ThenWorkspaceWriterSkipsStaleGeneration()
    {
        // Arrange
        await using var state = State.Value(this, () => ChatState.Empty);
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
        Assert.Equal(1, writer.EnqueueCount);
        Assert.Equal(new long[] { 1L }, writer.EnqueuedGenerations);
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
}
