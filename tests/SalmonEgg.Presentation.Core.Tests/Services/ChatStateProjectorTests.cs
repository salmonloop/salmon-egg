using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Tests.Localization;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public sealed class ChatStateProjectorTests
{
    [Fact]
    public async Task Apply_ChatOwnerComesFromBindingProfile()
    {
        var binding = new ConversationRemoteBindingState("conv-1", "remote-1", "profile-binding");
        var projector = new ChatStateProjector();
        var connectionState = ChatConnectionState.Empty with { SelectedProfileIntentId = "profile-store" };

        var projection = projector.Apply(ChatState.Empty, connectionState, "conv-1", binding);

        Assert.Equal("profile-binding", projection.ChatOwnerProfileId);
        Assert.Equal("profile-store", projection.SelectedProfileIntentId);
        Assert.Equal("remote-1", projection.RemoteSessionId);
    }

    [Fact]
    public void Apply_ChatOwnerIsNullWhenBindingMissing()
    {
        var projector = new ChatStateProjector();

        var projection = projector.Apply(ChatState.Empty, ChatConnectionState.Empty, "conv-2", binding: null);

        Assert.Null(projection.ChatOwnerProfileId);
    }

    [Fact]
    public void Apply_ReturnsNullRemoteSessionWhenBindingMissing()
    {
        var projector = new ChatStateProjector();
        var connectionState = ChatConnectionState.Empty with { SelectedProfileIntentId = "profile-store" };

        var projection = projector.Apply(ChatState.Empty, connectionState, "conv-3", binding: null);

        Assert.Null(projection.RemoteSessionId);
        Assert.Equal("profile-store", projection.SelectedProfileIntentId);
    }

    [Fact]
    public void Apply_ProjectsConnectionInstanceId()
    {
        var projector = new ChatStateProjector();
        var connectionState = ChatConnectionState.Empty with { ConnectionInstanceId = "conn-1" };

        var projection = projector.Apply(ChatState.Empty, connectionState, "conv-1", binding: null);

        Assert.Equal("conn-1", projection.ConnectionInstanceId);
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
        Assert.True(projection.IsPromptInFlight);
        Assert.False(projection.IsPromptSubmitInFlight);
        Assert.Equal(ChatTurnPhase.Thinking, projection.TurnPhase);
    }

    [Fact]
    public void Apply_ProjectsTurnStatusTextFromCoreStringResources()
    {
        var projector = new ChatStateProjector(new TestCoreStringLocalizer());
        var storeState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState(
                "conv-1",
                "turn-1",
                ChatTurnPhase.ToolRunning,
                DateTime.UtcNow,
                DateTime.UtcNow,
                ToolTitle: "read_file")
        };

        var projection = projector.Apply(storeState, ChatConnectionState.Empty, "conv-1", null);

        Assert.Equal("正在运行工具：read_file", projection.TurnStatusText);
    }

    [Theory]
    [InlineData(ChatTurnPhase.CreatingRemoteSession, true)]
    [InlineData(ChatTurnPhase.DispatchingPrompt, true)]
    [InlineData(ChatTurnPhase.WaitingForAgent, false)]
    public void Apply_DerivesPromptSubmitStateFromActiveTurnPhase(
        ChatTurnPhase phase,
        bool expectedSubmitInFlight)
    {
        var projector = new ChatStateProjector();
        var storeState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", phase, DateTime.UtcNow, DateTime.UtcNow)
        };

        var projection = projector.Apply(storeState, ChatConnectionState.Empty, "conv-1", null);

        Assert.Equal(expectedSubmitInFlight, projection.IsPromptSubmitInFlight);
        Assert.True(projection.IsPromptInFlight);
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
        Assert.False(projection.IsPromptInFlight);
        Assert.Equal(ChatTurnPhase.Cancelled, projection.TurnPhase);
        Assert.Equal("Cancelled", projection.TurnStatusText);
    }

    [Fact]
    public void Apply_AgentDisplayUsesForegroundTransportProfileWhenNoBinding()
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
            SelectedProfileIntentId = "profile-next",
            ForegroundTransportProfileId = "profile-committed"
        };

        var projection = projector.Apply(storeState, connectionState, "conv-1", null);

        Assert.Equal("Committed Agent", projection.AgentName);
        Assert.Equal("1.0.0", projection.AgentVersion);
    }

    [Fact]
    public void Apply_AgentDisplayPrefersBindingProfileOverForegroundTransport()
    {
        var projector = new ChatStateProjector();
        var storeState = ChatState.Empty with
        {
            AgentProfileId = "profile-binding",
            AgentName = "Binding Agent",
            AgentVersion = "2.0.0"
        };
        var connectionState = ChatConnectionState.Empty with
        {
            SelectedProfileIntentId = "profile-other",
            ForegroundTransportProfileId = "profile-transport"
        };
        var binding = new ConversationRemoteBindingState("conv-1", "remote-1", "profile-binding");

        var projection = projector.Apply(storeState, connectionState, "conv-1", binding);

        Assert.Equal("Binding Agent", projection.AgentName);
        Assert.Equal("2.0.0", projection.AgentVersion);
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
                    true)),
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
        Assert.Single(projection.AvailableModes);
        Assert.Equal("agent", projection.SelectedModeId);
        Assert.Single(projection.ConfigOptions);
        Assert.True(projection.ShowConfigOptionsPanel);
    }

}
