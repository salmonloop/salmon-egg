using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Domain.Models.Conversation;
using System.Collections.Immutable;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Mvux;

public class ChatReducerTests
{
    [Fact]
    public void GivenInitialState_WhenSetSelectedConversation_ThenConversationIdIsUpdated()
    {
        // Arrange
        var initialState = new ChatState();
        var conversationId = "test-conv-123";
        var action = new SelectConversationAction(conversationId);

        // Act
        var newState = ChatReducer.Reduce(initialState, action);

        // Assert
        Assert.Equal(conversationId, newState.SelectedConversationId);
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
    public void GivenState_WhenSelectProfile_ThenSelectedProfileIdIsUpdated()
    {
        // Arrange
        var initialState = new ChatState(SelectedAcpProfileId: null);
        var profileId = "profile-1";
        var action = new SelectProfileAction(profileId);

        // Act
        var newState = ChatReducer.Reduce(initialState, action);

        // Assert
        Assert.Equal(profileId, newState.SelectedAcpProfileId);
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
    public void GivenConversationState_WhenSelectConversation_ThenConversationSliceIsCleared()
    {
        var initialState = new ChatState(
            SelectedConversationId: "conv-1",
            Transcript: ImmutableList.Create(new ConversationMessageSnapshot { Id = "m-1", TextContent = "hello", ContentType = "text" }),
            PlanEntries: ImmutableList.Create(new ConversationPlanEntrySnapshot { Content = "step-1" }),
            ShowPlanPanel: true,
            PlanTitle: "plan");

        var newState = ChatReducer.Reduce(initialState, new SelectConversationAction("conv-2"));

        Assert.Equal("conv-2", newState.SelectedConversationId);
        Assert.Null(newState.Transcript);
        Assert.Null(newState.PlanEntries);
        Assert.False(newState.ShowPlanPanel);
        Assert.Null(newState.PlanTitle);
    }

    [Fact]
    public void GivenDifferentSelectedConversation_WhenHydrating_ThenReducerIgnoresStaleSnapshot()
    {
        var initialState = new ChatState(SelectedConversationId: "conv-1");
        var action = new HydrateConversationAction(
            "conv-2",
            ImmutableList.Create(new ConversationMessageSnapshot { Id = "m-1", TextContent = "stale", ContentType = "text" }),
            ImmutableList.Create(new ConversationPlanEntrySnapshot { Content = "step-1" }),
            true,
            "plan");

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Equal("conv-1", newState.SelectedConversationId);
        Assert.True(newState.Transcript is null or { Count: 0 });
        Assert.True(newState.PlanEntries is null or { Count: 0 });
        Assert.False(newState.ShowPlanPanel);
        Assert.Null(newState.PlanTitle);
    }
}
