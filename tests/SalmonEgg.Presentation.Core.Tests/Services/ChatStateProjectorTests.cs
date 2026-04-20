using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public sealed class ChatStateProjectorTests
{
    [Fact]
    public async Task Apply_SelectedProfilePrefersStoreOverBinding()
    {
        var binding = new ConversationRemoteBindingState("conv-1", "remote-1", "profile-binding");
        var projector = new ChatStateProjector();
        var connectionState = ChatConnectionState.Empty with { SelectedProfileId = "profile-store" };

        var projection = projector.Apply(ChatState.Empty, connectionState, "conv-1", binding);

        Assert.Equal("profile-store", projection.SelectedProfileId);
        Assert.Equal("remote-1", projection.RemoteSessionId);
    }

    [Fact]
    public void Apply_DoesNotFallbackToBindingProfileWhenConnectionStoreProfileMissing()
    {
        var binding = new ConversationRemoteBindingState("conv-2", "remote-2", "profile-binding");
        var projector = new ChatStateProjector();

        var projection = projector.Apply(ChatState.Empty, ChatConnectionState.Empty, "conv-2", binding);

        Assert.Null(projection.SelectedProfileId);
        Assert.Equal("remote-2", projection.RemoteSessionId);
    }

    [Fact]
    public void Apply_ReturnsNullRemoteSessionWhenBindingMissing()
    {
        var projector = new ChatStateProjector();
        var connectionState = ChatConnectionState.Empty with { SelectedProfileId = "profile-store" };

        var projection = projector.Apply(ChatState.Empty, connectionState, "conv-3", binding: null);

        Assert.Null(projection.RemoteSessionId);
        Assert.Equal("profile-store", projection.SelectedProfileId);
    }

    [Fact]
    public void Apply_ProjectsTailStatusFromActiveTurn()
    {
        var projector = new ChatStateProjector();
        var storeState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow)
        };

        var projection = projector.Apply(storeState, ChatConnectionState.Empty, "conv-1", null);

        Assert.True(projection.IsTurnStatusVisible);
        Assert.Equal("Thinking...", projection.TurnStatusText);
        Assert.True(projection.IsTurnStatusRunning);
        Assert.Equal(ChatTurnPhase.Thinking, projection.TurnPhase);
    }

    [Fact]
    public void Apply_HidesTurnStatusWhenActiveTurnBelongsToDifferentConversation()
    {
        var projector = new ChatStateProjector();
        var storeState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow)
        };

        var projection = projector.Apply(storeState, ChatConnectionState.Empty, "conv-2", null);

        Assert.False(projection.IsTurnStatusVisible);
        Assert.False(projection.IsTurnStatusRunning);
        Assert.Null(projection.TurnPhase);
        Assert.Equal(string.Empty, projection.TurnStatusText);
    }

    [Fact]
    public void Apply_PreservesCancelledTurnVisibility()
    {
        var projector = new ChatStateProjector();
        var storeState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Cancelled, DateTime.UtcNow, DateTime.UtcNow)
        };

        var projection = projector.Apply(storeState, ChatConnectionState.Empty, "conv-1", null);

        Assert.True(projection.IsTurnStatusVisible);
        Assert.False(projection.IsTurnStatusRunning);
        Assert.Equal(ChatTurnPhase.Cancelled, projection.TurnPhase);
        Assert.Equal("Cancelled", projection.TurnStatusText);
    }

    [Fact]
    public void Apply_AgentDisplayPrefersCommittedProfileOwnershipOverSelectedIntent()
    {
        var projector = new ChatStateProjector();
        var storeState = ChatState.Empty with
        {
            AgentProfileId = "profile-committed",
            AgentName = "Committed Agent",
            AgentVersion = "1.0.0"
        };
        var connectionState = ChatConnectionState.Empty with
        {
            SelectedProfileId = "profile-next",
            CommittedProfileId = "profile-committed"
        };

        var projection = projector.Apply(storeState, connectionState, "conv-1", null);

        Assert.Equal("Committed Agent", projection.AgentName);
        Assert.Equal("1.0.0", projection.AgentVersion);
    }

    [Fact]
    public void Apply_AgentDisplayFallsBackToSelectedProfileBeforeCommitExists()
    {
        var projector = new ChatStateProjector();
        var storeState = ChatState.Empty with
        {
            AgentProfileId = "profile-selected",
            AgentName = "Selected Agent",
            AgentVersion = "1.0.0"
        };
        var connectionState = ChatConnectionState.Empty with
        {
            SelectedProfileId = "profile-selected",
            CommittedProfileId = null
        };

        var projection = projector.Apply(storeState, connectionState, "conv-1", null);

        Assert.Equal("Selected Agent", projection.AgentName);
        Assert.Equal("1.0.0", projection.AgentVersion);
    }

    [Fact]
    public void Apply_ProjectsHydratedConversationSlicesInsteadOfOnlyTopLevelCache()
    {
        var projector = new ChatStateProjector();
        var storeState = ChatState.Empty with
        {
            HydratedConversationId = "conv-2",
            Transcript = ImmutableList.Create(new ConversationMessageSnapshot { Id = "stale", ContentType = "text", TextContent = "stale" }),
            PlanEntries = ImmutableList<ConversationPlanEntrySnapshot>.Empty,
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "conv-2",
                new ConversationContentSlice(
                    ImmutableList.Create(new ConversationMessageSnapshot { Id = "fresh", ContentType = "text", TextContent = "fresh" }),
                    ImmutableList.Create(new ConversationPlanEntrySnapshot { Content = "step-1" }),
                    true,
                    "plan")),
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "conv-2",
                new ConversationSessionStateSlice(
                    ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" }),
                    "agent",
                    ImmutableList.Create(new ConversationConfigOptionSnapshot { Id = "mode", Name = "Mode", SelectedValue = "agent" }),
                    true,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    null,
                    null))
        };

        var projection = projector.Apply(storeState, ChatConnectionState.Empty, "conv-2", null);

        Assert.Single(projection.Transcript);
        Assert.Equal("fresh", projection.Transcript[0].TextContent);
        Assert.Single(projection.PlanEntries);
        Assert.True(projection.ShowPlanPanel);
        Assert.Equal("plan", projection.PlanTitle);
        Assert.Single(projection.AvailableModes);
        Assert.Equal("agent", projection.SelectedModeId);
        Assert.Single(projection.ConfigOptions);
        Assert.True(projection.ShowConfigOptionsPanel);
    }

}
