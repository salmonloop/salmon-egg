using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public class ChatStateProjectorTests
{
    [Fact]
    public void Apply_ProjectsChatOwnerFromBinding()
    {
        var projector = new ChatStateProjector();
        var state = new ChatState(
            HydratedConversationId: "session-1",
            Transcript: ImmutableList<ConversationMessageSnapshot>.Empty,
            PlanEntries: ImmutableList<ConversationPlanEntrySnapshot>.Empty);

        var projection = projector.Apply(
            state,
            ChatConnectionState.Empty,
            "session-1",
            new ConversationRemoteBindingState("session-1", "remote-1", "profile-a"));

        Assert.Equal("profile-a", projection.ChatOwnerProfileId);
        Assert.Equal("remote-1", projection.RemoteSessionId);
    }

    [Fact]
    public void Apply_SetsSelectedProfileIntentIdSeparatelyFromChatOwner()
    {
        var projector = new ChatStateProjector();
        var state = new ChatState(
            HydratedConversationId: "session-1",
            Transcript: ImmutableList<ConversationMessageSnapshot>.Empty,
            PlanEntries: ImmutableList<ConversationPlanEntrySnapshot>.Empty);

        var projection = projector.Apply(
            state,
            ChatConnectionState.Empty with { SelectedProfileIntentId = "profile-b" },
            "session-1",
            new ConversationRemoteBindingState("session-1", "remote-1", "profile-a"));

        Assert.Equal("profile-a", projection.ChatOwnerProfileId);
        Assert.Equal("profile-b", projection.SelectedProfileIntentId);
        Assert.Equal("remote-1", projection.RemoteSessionId);
    }

    [Fact]
    public void Apply_DoesNotProjectChatOwnerOrForegroundAsSelectedProfileIntentWhenSelectionIntentIsMissing()
    {
        var projector = new ChatStateProjector();
        var state = new ChatState(
            HydratedConversationId: "session-1",
            Transcript: ImmutableList<ConversationMessageSnapshot>.Empty,
            PlanEntries: ImmutableList<ConversationPlanEntrySnapshot>.Empty);

        var projection = projector.Apply(
            state,
            ChatConnectionState.Empty with { ForegroundTransportProfileId = "profile-foreground" },
            "session-1",
            new ConversationRemoteBindingState("session-1", "remote-1", "profile-owner"));

        Assert.Equal("profile-owner", projection.ChatOwnerProfileId);
        Assert.Equal("profile-foreground", projection.ForegroundTransportProfileId);
        Assert.Null(projection.SelectedProfileIntentId);
    }
}
