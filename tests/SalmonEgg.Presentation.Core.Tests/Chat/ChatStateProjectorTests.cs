using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public class ChatStateProjectorTests
{
    [Fact]
    public void Apply_ProjectsRemoteBindingFromWorkspaceInput()
    {
        var projector = new ChatStateProjector();
        var state = new ChatState(
            SelectedConversationId: "session-1",
            SelectedAcpProfileId: null,
            Transcript: ImmutableList<ConversationMessageSnapshot>.Empty,
            PlanEntries: ImmutableList<ConversationPlanEntrySnapshot>.Empty);

        var projection = projector.Apply(
            state,
            new ConversationRemoteBindingState("session-1", "remote-1", "profile-a"));

        Assert.Equal("profile-a", projection.SelectedProfileId);
        Assert.Equal("remote-1", projection.RemoteSessionId);
    }

    [Fact]
    public void Apply_PrefersSelectedProfileOverBindingFallback()
    {
        var projector = new ChatStateProjector();
        var state = new ChatState(
            SelectedConversationId: "session-1",
            SelectedAcpProfileId: "profile-b",
            Transcript: ImmutableList<ConversationMessageSnapshot>.Empty,
            PlanEntries: ImmutableList<ConversationPlanEntrySnapshot>.Empty);

        var projection = projector.Apply(
            state,
            new ConversationRemoteBindingState("session-1", "remote-1", "profile-a"));

        Assert.Equal("profile-b", projection.SelectedProfileId);
        Assert.Equal("remote-1", projection.RemoteSessionId);
    }
}
