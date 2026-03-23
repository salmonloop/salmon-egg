using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
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
        Assert.True(projection.IsThinking);
    }
}
