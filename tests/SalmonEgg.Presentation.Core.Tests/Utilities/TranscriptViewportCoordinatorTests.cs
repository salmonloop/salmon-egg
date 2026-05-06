using SalmonEgg.Presentation.Utilities;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class TranscriptViewportCoordinatorTests
{
    [Fact]
    public void NonBottomWithoutUserIntent_DoesNotDetachAutoFollow()
    {
        var sut = new TranscriptViewportCoordinator();
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-1", generation: 1));
        sut.Handle(new TranscriptViewportEvent.TranscriptAppended("conv-1", generation: 1, addedCount: 20));

        var command = sut.Handle(new TranscriptViewportEvent.ViewportFactChanged(
            "conv-1",
            generation: 1,
            new TranscriptViewportFact(
                HasItems: true,
                IsReady: true,
                IsAtBottom: false,
                IsProgrammaticScrollInFlight: false)));

        Assert.Equal(TranscriptViewportState.Settling, sut.State);
        Assert.True(sut.IsAutoFollowAttached);
        Assert.Equal(TranscriptViewportCommandKind.IssueScrollToBottom, command.Kind);
    }

    [Fact]
    public void UserIntentScroll_DetachesAutoFollow()
    {
        var sut = new TranscriptViewportCoordinator();
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-1", generation: 1));

        var command = sut.Handle(new TranscriptViewportEvent.UserIntentScroll("conv-1", generation: 1));

        Assert.Equal(TranscriptViewportState.DetachedByUser, sut.State);
        Assert.False(sut.IsAutoFollowAttached);
        Assert.Equal(TranscriptViewportCommandKind.MarkAutoFollowDetached, command.Kind);
    }

    [Fact]
    public void SessionActivated_ThenReadyBottom_TransitionsToFollowing_AndIssuesAttach()
    {
        var sut = new TranscriptViewportCoordinator();
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-1", 1));

        var command = sut.Handle(new TranscriptViewportEvent.ViewportFactChanged(
            "conv-1",
            1,
            new TranscriptViewportFact(
                HasItems: true,
                IsReady: true,
                IsAtBottom: true,
                IsProgrammaticScrollInFlight: false)));

        Assert.Equal(TranscriptViewportState.Following, sut.State);
        Assert.Equal(TranscriptViewportCommandKind.MarkAutoFollowAttached, command.Kind);
    }

    [Fact]
    public void DetachedByUser_DoesNotReattachUntilViewportLeavesAndReturnsToBottom()
    {
        var sut = new TranscriptViewportCoordinator();
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-1", 1));
        sut.Handle(new TranscriptViewportEvent.UserIntentScroll("conv-1", 1));

        var stillDetached = sut.Handle(new TranscriptViewportEvent.ViewportFactChanged(
            "conv-1",
            1,
            new TranscriptViewportFact(
                HasItems: true,
                IsReady: true,
                IsAtBottom: true,
                IsProgrammaticScrollInFlight: false)));

        var awayFromBottom = sut.Handle(new TranscriptViewportEvent.ViewportFactChanged(
            "conv-1",
            1,
            new TranscriptViewportFact(
                HasItems: true,
                IsReady: true,
                IsAtBottom: false,
                IsProgrammaticScrollInFlight: false)));

        var returnedToBottom = sut.Handle(new TranscriptViewportEvent.ViewportFactChanged(
            "conv-1",
            1,
            new TranscriptViewportFact(
                HasItems: true,
                IsReady: true,
                IsAtBottom: true,
                IsProgrammaticScrollInFlight: false)));

        Assert.Equal(TranscriptViewportCommandKind.None, stillDetached.Kind);
        Assert.Equal(TranscriptViewportCommandKind.None, awayFromBottom.Kind);
        Assert.Equal(TranscriptViewportCommandKind.MarkAutoFollowAttached, returnedToBottom.Kind);
        Assert.Equal(TranscriptViewportState.Following, sut.State);
        Assert.True(sut.IsAutoFollowAttached);
    }

    [Fact]
    public void StaleGenerationEvent_IsIgnored()
    {
        var sut = new TranscriptViewportCoordinator();
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-1", 3));

        var command = sut.Handle(new TranscriptViewportEvent.UserIntentScroll("conv-1", 2));

        Assert.Equal(TranscriptViewportCommandKind.None, command.Kind);
        Assert.Equal(TranscriptViewportState.Settling, sut.State);
        Assert.True(sut.IsAutoFollowAttached);
    }

    [Fact]
    public void DifferentConversationEvent_IsIgnored()
    {
        var sut = new TranscriptViewportCoordinator();
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-1", 3));

        var command = sut.Handle(new TranscriptViewportEvent.ViewportFactChanged(
            "conv-2",
            3,
            new TranscriptViewportFact(
                HasItems: true,
                IsReady: true,
                IsAtBottom: false,
                IsProgrammaticScrollInFlight: false)));

        Assert.Equal(TranscriptViewportCommandKind.None, command.Kind);
        Assert.Equal(TranscriptViewportState.Settling, sut.State);
    }
}
