using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public sealed class ChatStateProjectorTests
{
    [Fact]
    public void Apply_SelectedProfilePrefersStoreOverBinding()
    {
        var state = new ChatState(
            SelectedConversationId: "conv-1",
            SelectedAcpProfileId: "profile-store");
        var binding = new ConversationRemoteBindingState("conv-1", "remote-1", "profile-binding");
        var projector = new ChatStateProjector();

        var projection = projector.Apply(state, binding);

        Assert.Equal("profile-store", projection.SelectedProfileId);
        Assert.Equal("remote-1", projection.RemoteSessionId);
    }

    [Fact]
    public void Apply_FallsBackToBindingProfileWhenStoreProfileMissing()
    {
        var state = new ChatState(SelectedConversationId: "conv-2");
        var binding = new ConversationRemoteBindingState("conv-2", "remote-2", "profile-binding");
        var projector = new ChatStateProjector();

        var projection = projector.Apply(state, binding);

        Assert.Equal("profile-binding", projection.SelectedProfileId);
        Assert.Equal("remote-2", projection.RemoteSessionId);
    }

    [Fact]
    public void Apply_ReturnsNullRemoteSessionWhenBindingMissing()
    {
        var state = new ChatState(SelectedConversationId: "conv-3", SelectedAcpProfileId: "profile-store");
        var projector = new ChatStateProjector();

        var projection = projector.Apply(state, binding: null);

        Assert.Null(projection.RemoteSessionId);
        Assert.Equal("profile-store", projection.SelectedProfileId);
    }
}
