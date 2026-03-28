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
        Assert.Equal(conversationId, newState.HydratedConversationId);
    }

    [Fact]
    public void ChatState_DoesNotExposeLegacySelectedConversationProperty()
    {
        Assert.Null(typeof(ChatState).GetProperty("SelectedConversationId"));
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
            AvailableModes: ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" }),
            SelectedModeId: "agent",
            ConfigOptions: ImmutableList.Create(new ConversationConfigOptionSnapshot { Id = "mode", Name = "Mode", SelectedValue = "agent" }),
            ShowConfigOptionsPanel: true,
            ShowPlanPanel: true,
            PlanTitle: "plan");

        var newState = ChatReducer.Reduce(initialState, new SelectConversationAction("conv-2"));

        Assert.Equal("conv-2", newState.HydratedConversationId);
        Assert.Null(newState.Transcript);
        Assert.Null(newState.PlanEntries);
        Assert.Null(newState.AvailableModes);
        Assert.Null(newState.SelectedModeId);
        Assert.Null(newState.ConfigOptions);
        Assert.False(newState.ShowConfigOptionsPanel);
        Assert.False(newState.ShowPlanPanel);
        Assert.Null(newState.PlanTitle);
    }

    [Fact]
    public void GivenHydratingConversation_WhenSelectConversation_ThenHydrationFlagIsCleared()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            IsHydrating = true
        };

        var newState = ChatReducer.Reduce(initialState, new SelectConversationAction("conv-2"));

        Assert.Equal("conv-2", newState.HydratedConversationId);
        Assert.False(newState.IsHydrating);
    }

    [Fact]
    public void SetConversationSessionState_ProjectsOnlyForHydratedConversation()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Generation = 11
        };

        var action = new SetConversationSessionStateAction(
            "conv-1",
            ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" }),
            "agent",
            ImmutableList.Create(new ConversationConfigOptionSnapshot { Id = "mode", Name = "Mode", SelectedValue = "agent" }),
            true);

        var projected = ChatReducer.Reduce(initialState, action);
        Assert.Equal(12, projected.Generation);
        Assert.Single(projected.AvailableModes!);
        Assert.Equal("agent", projected.SelectedModeId);
        Assert.Single(projected.ConfigOptions!);
        Assert.True(projected.ShowConfigOptionsPanel);

        var stale = ChatReducer.Reduce(initialState, action with { ConversationId = "conv-2" });
        Assert.Equal(initialState.Generation, stale.Generation);
        Assert.Null(stale.AvailableModes);
        Assert.Null(stale.ConfigOptions);
    }

    [Fact]
    public void MergeConversationSessionState_PreservesExistingValuesForPartialDelta()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Generation = 21,
            AvailableModes = ImmutableList.Create(
                new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" },
                new ConversationModeOptionSnapshot { ModeId = "plan", ModeName = "Plan" }),
            SelectedModeId = "agent",
            ConfigOptions = ImmutableList.Create(
                new ConversationConfigOptionSnapshot { Id = "mode", Name = "Mode", SelectedValue = "agent" }),
            ShowConfigOptionsPanel = true
        };

        var projected = ChatReducer.Reduce(initialState, new MergeConversationSessionStateAction(
            "conv-1",
            SelectedModeId: "plan",
            HasSelectedModeId: true));

        Assert.Equal(22, projected.Generation);
        Assert.NotNull(projected.AvailableModes);
        Assert.Equal(2, projected.AvailableModes!.Count);
        Assert.Equal("plan", projected.SelectedModeId);
        Assert.NotNull(projected.ConfigOptions);
        Assert.Single(projected.ConfigOptions!);
        Assert.True(projected.ShowConfigOptionsPanel);

        var cleared = ChatReducer.Reduce(projected, new MergeConversationSessionStateAction(
            "conv-1",
            AvailableModes: ImmutableList<ConversationModeOptionSnapshot>.Empty,
            SelectedModeId: null,
            HasSelectedModeId: true));

        Assert.Empty(cleared.AvailableModes!);
        Assert.Null(cleared.SelectedModeId);
        Assert.Single(cleared.ConfigOptions!);
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
    public void AdvanceTurnPhase_IgnoresConversationMismatchEvenWhenTurnIdMatches()
    {
        var initialState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow),
            Generation = 4
        };
        var action = new AdvanceTurnPhaseAction("conv-remote", "turn-1", ChatTurnPhase.Responding);

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Equal(4, newState.Generation);
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

    [Fact]
    public void CompleteTurn_DoesNotOverride_FailedOrCancelled()
    {
        var failedState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Failed, DateTime.UtcNow, DateTime.UtcNow)
        };
        var action = new CompleteTurnAction("conv-1", "turn-1");

        var newState = ChatReducer.Reduce(failedState, action);

        Assert.Equal(ChatTurnPhase.Failed, newState.ActiveTurn!.Phase);

        var cancelledState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Cancelled, DateTime.UtcNow, DateTime.UtcNow)
        };
        var newState2 = ChatReducer.Reduce(cancelledState, action);

        Assert.Equal(ChatTurnPhase.Cancelled, newState2.ActiveTurn!.Phase);
    }

    [Fact]
    public void AdvanceTurnPhase_DoesNotOverrideTerminalPhase()
    {
        var completedState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Completed, DateTime.UtcNow, DateTime.UtcNow)
        };

        var newState = ChatReducer.Reduce(
            completedState,
            new AdvanceTurnPhaseAction("conv-1", "turn-1", ChatTurnPhase.Responding));

        Assert.Equal(ChatTurnPhase.Completed, newState.ActiveTurn!.Phase);
    }
}
