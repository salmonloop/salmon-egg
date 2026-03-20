using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;
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
}
