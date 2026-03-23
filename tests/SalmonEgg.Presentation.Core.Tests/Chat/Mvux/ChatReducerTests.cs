using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Domain.Models.Conversation;
using System.Collections.Immutable;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Mvux;

public class ChatReducerTests
{
    [Fact]
    public void GivenInitialState_WhenSetSelectedConversation_ThenHydratedConversationIdIsUpdated()
    {
        // Arrange
        var initialState = new ChatState();
        var conversationId = "test-conv-123";
        var action = new SelectConversationAction(conversationId);

        // Act
        var newState = ChatReducer.Reduce(initialState, action);

        // Assert
        Assert.Null(newState.SelectedConversationId);
        Assert.Equal(conversationId, newState.HydratedConversationId);
    }

    [Fact]
    public void GivenState_WhenSetPromptInFlight_ThenIsPromptInFlightIsTrue()
    {
        // Arrange
        var initialState = new ChatState(IsPromptInFlight: false);
        var action = new SetPromptInFlightAction(true);

        // Act
        var newState = ChatReducer.Reduce(initialState, action);

        // Assert
        Assert.True(newState.IsPromptInFlight);
    }

    [Fact]
    public void GivenState_WhenSetDraftText_ThenDraftTextIsUpdated()
    {
        // Arrange
        var initialState = new ChatState(DraftText: string.Empty);
        var action = new SetDraftTextAction("hello");

        // Act
        var newState = ChatReducer.Reduce(initialState, action);

        // Assert
        Assert.Equal("hello", newState.DraftText);
    }

    [Fact]
    public void GivenEmptyState_WhenSetBindingSlice_ThenBindingAndGenerationAreUpdated()
    {
        // Arrange
        var initialState = ChatState.Empty;
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");

        // Act
        var newState = ChatReducer.Reduce(initialState, new SetBindingSliceAction(binding));

        // Assert
        Assert.Equal(binding, newState.ResolveBinding("conv-1"));
        Assert.Equal(1, newState.Generation);
    }

    [Fact]
    public void GivenMultipleBindings_WhenSelectingConversation_ThenMatchingBindingProjectsFromDictionary()
    {
        var initialState = ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-2"))
        };

        var newState = ChatReducer.Reduce(initialState, new SelectConversationAction("conv-2"));

        Assert.Equal("conv-2", newState.HydratedConversationId);
        Assert.Equal(new ConversationBindingSlice("conv-2", "remote-2", "profile-2"), newState.Binding);
    }

    [Fact]
    public void GivenState_WhenRuntimeMutationOccurs_ThenGenerationIncrements()
    {
        // Arrange
        var initialState = ChatState.Empty with { Generation = 2 };

        // Act
        var newState = ChatReducer.Reduce(initialState, new SetDraftTextAction("hi"));

        // Assert
        Assert.Equal(3, newState.Generation);
    }

    [Fact]
    public void GivenState_WhenGuardedActionIsNoOp_ThenGenerationDoesNotIncrement()
    {
        // Arrange
        var initialState = ChatState.Empty with { HydratedConversationId = "conv-1", Generation = 5 };
        var message = new ConversationMessageSnapshot
        {
            Id = "m-1",
            ContentType = "text",
            TextContent = "hello"
        };

        // Act
        var newState = ChatReducer.Reduce(initialState, new UpsertTranscriptMessageAction("conv-2", message));

        // Assert
        Assert.Equal(initialState.Generation, newState.Generation);
    }

    [Fact]
    public void GivenConversationState_WhenSelectConversation_ThenConversationSliceIsCleared()
    {
        var initialState = new ChatState(
            HydratedConversationId: "conv-1",
            Transcript: ImmutableList.Create(new ConversationMessageSnapshot { Id = "m-1", TextContent = "hello", ContentType = "text" }),
            PlanEntries: ImmutableList.Create(new ConversationPlanEntrySnapshot { Content = "step-1" }),
            ShowPlanPanel: true,
            PlanTitle: "plan");

        var newState = ChatReducer.Reduce(initialState, new SelectConversationAction("conv-2"));

        Assert.Null(newState.SelectedConversationId);
        Assert.Equal("conv-2", newState.HydratedConversationId);
        Assert.Null(newState.Transcript);
        Assert.Null(newState.PlanEntries);
        Assert.False(newState.ShowPlanPanel);
        Assert.Null(newState.PlanTitle);
    }

    [Fact]
    public void GivenDifferentSelectedConversation_WhenHydrating_ThenReducerIgnoresStaleSnapshot()
    {
        var initialState = new ChatState(HydratedConversationId: "conv-1", Generation: 7);
        var action = new HydrateConversationAction(
            "conv-2",
            ImmutableList.Create(new ConversationMessageSnapshot { Id = "m-1", TextContent = "stale", ContentType = "text" }),
            ImmutableList.Create(new ConversationPlanEntrySnapshot { Content = "step-1" }),
            true,
            "plan");

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Null(newState.SelectedConversationId);
        Assert.Equal("conv-1", newState.HydratedConversationId);
        Assert.True(newState.Transcript is null or { Count: 0 });
        Assert.True(newState.PlanEntries is null or { Count: 0 });
        Assert.False(newState.ShowPlanPanel);
        Assert.Null(newState.PlanTitle);
        Assert.Equal(initialState.Generation, newState.Generation);
    }

    [Fact]
    public void BeginTurn_SetsActiveTurnAndGeneration()
    {
        var initialState = ChatState.Empty with { Generation = 10 };
        var action = new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent);

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Equal(11, newState.Generation);
        Assert.NotNull(newState.ActiveTurn);
        Assert.Equal("turn-1", newState.ActiveTurn!.TurnId);
        Assert.Equal(ChatTurnPhase.WaitingForAgent, newState.ActiveTurn.Phase);
    }

    [Fact]
    public void AdvanceTurnPhase_IgnoresStaleTurnId()
    {
        var initialState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-current", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow),
            Generation = 10
        };
        var action = new AdvanceTurnPhaseAction("conv-1", "turn-stale", ChatTurnPhase.Responding);

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Equal(10, newState.Generation);
        Assert.Equal(ChatTurnPhase.Thinking, newState.ActiveTurn!.Phase);
    }

    [Fact]
    public void SelectConversation_ClearsActiveTurnForPreviousConversation()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow)
        };
        var action = new SelectConversationAction("conv-2");

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Null(newState.ActiveTurn);
        Assert.Equal("conv-2", newState.HydratedConversationId);
    }
}
