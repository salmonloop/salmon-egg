using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class TranscriptFollowControllerTests
{
    [Fact]
    public void Activate_FollowsBottom_AndRequestsScrollToEnd()
    {
        var sut = new TranscriptFollowController();

        var request = sut.Activate("conv-1", activationGeneration: 1);

        Assert.Equal(TranscriptFollowMode.FollowingBottom, sut.State.Mode);
        Assert.Equal(TranscriptScrollRequestKind.ScrollToEnd, request.Kind);
        Assert.Equal("conv-1", request.ConversationId);
        Assert.Equal(1, request.ActivationGeneration);
    }

    [Fact]
    public void Observe_LeaveBottomWithRestorableKey_PinsWithoutScroll()
    {
        var sut = new TranscriptFollowController();
        _ = sut.Activate("conv-1", 1);

        var request = sut.Observe(new TranscriptViewportObservation(
            "conv-1",
            1,
            HasItems: true,
            IsAtBottom: false,
            ProgrammaticScrollInFlight: false,
            TopVisibleItemKey: "msg:a"));

        Assert.Equal(TranscriptScrollRequestKind.None, request.Kind);
        Assert.Equal(TranscriptFollowMode.PinnedToItem, sut.State.Mode);
        Assert.Equal("msg:a", sut.State.PinnedItemKey);
    }

    [Fact]
    public void ContentChanged_WhileFollowing_RequestsScrollToEnd()
    {
        var sut = new TranscriptFollowController();
        _ = sut.Activate("conv-1", 1);

        var request = sut.OnContentChanged("conv-1", 1, pinStillResolvable: true);

        Assert.Equal(TranscriptScrollRequestKind.ScrollToEnd, request.Kind);
    }

    [Fact]
    public void ContentChanged_WhilePinned_DoesNotScrollToEnd()
    {
        var sut = new TranscriptFollowController();
        _ = sut.Activate("conv-1", 1);
        _ = sut.Observe(new TranscriptViewportObservation(
            "conv-1", 1, true, IsAtBottom: false, false, "msg:a"));

        var request = sut.OnContentChanged("conv-1", 1, pinStillResolvable: true, pinIsVisible: true);

        Assert.Equal(TranscriptScrollRequestKind.None, request.Kind);
        Assert.Equal(TranscriptFollowMode.PinnedToItem, sut.State.Mode);
    }

    [Fact]
    public void ContentChanged_WhilePinnedAndNotVisible_Reanchors()
    {
        var sut = new TranscriptFollowController();
        _ = sut.Activate("conv-1", 1);
        _ = sut.Observe(new TranscriptViewportObservation(
            "conv-1", 1, true, IsAtBottom: false, false, "msg:a"));

        var request = sut.OnContentChanged("conv-1", 1, pinStillResolvable: true, pinIsVisible: false);

        Assert.Equal(TranscriptScrollRequestKind.ScrollIntoView, request.Kind);
        Assert.Equal("msg:a", request.ItemKey);
    }

    [Fact]
    public void ContentChanged_WhenPinnedItemMissing_FallsBackToBottom()
    {
        var sut = new TranscriptFollowController();
        _ = sut.Activate("conv-1", 1);
        _ = sut.Observe(new TranscriptViewportObservation(
            "conv-1", 1, true, IsAtBottom: false, false, "msg:a"));

        var request = sut.OnContentChanged("conv-1", 1, pinStillResolvable: false);

        Assert.Equal(TranscriptFollowMode.FollowingBottom, sut.State.Mode);
        Assert.Null(sut.State.PinnedItemKey);
        Assert.Equal(TranscriptScrollRequestKind.ScrollToEnd, request.Kind);
    }

    [Fact]
    public void Observe_StaleGeneration_IsIgnored()
    {
        var sut = new TranscriptFollowController();
        _ = sut.Activate("conv-1", 1);

        var request = sut.Observe(new TranscriptViewportObservation(
            "conv-1", 2, true, IsAtBottom: false, false, "msg:a"));

        Assert.Equal(TranscriptScrollRequestKind.None, request.Kind);
        Assert.Equal(TranscriptFollowMode.FollowingBottom, sut.State.Mode);
    }

    [Fact]
    public void Observe_ReturnToBottom_ReattachesFollow()
    {
        var sut = new TranscriptFollowController();
        _ = sut.Activate("conv-1", 1);
        _ = sut.Observe(new TranscriptViewportObservation(
            "conv-1", 1, true, IsAtBottom: false, false, "msg:a"));

        var request = sut.Observe(new TranscriptViewportObservation(
            "conv-1", 1, true, IsAtBottom: true, false, "msg:z"));

        Assert.Equal(TranscriptFollowMode.FollowingBottom, sut.State.Mode);
        Assert.Null(sut.State.PinnedItemKey);
        Assert.Equal(TranscriptScrollRequestKind.None, request.Kind);
    }

    [Fact]
    public void JumpToLatest_ForcesFollowingBottom()
    {
        var sut = new TranscriptFollowController();
        _ = sut.Activate("conv-1", 1);
        _ = sut.Observe(new TranscriptViewportObservation(
            "conv-1", 1, true, IsAtBottom: false, false, "msg:a"));

        var request = sut.JumpToLatest();

        Assert.Equal(TranscriptFollowMode.FollowingBottom, sut.State.Mode);
        Assert.Equal(TranscriptScrollRequestKind.ScrollToEnd, request.Kind);
    }

    [Fact]
    public void Observe_OffBottomWithoutRestorableKey_DoesNotPin()
    {
        var sut = new TranscriptFollowController();
        _ = sut.Activate("conv-1", 1);

        var request = sut.Observe(new TranscriptViewportObservation(
            "conv-1", 1, true, IsAtBottom: false, false, TopVisibleItemKey: "ephemeral:0:text:in"));

        Assert.Equal(TranscriptScrollRequestKind.None, request.Kind);
        Assert.Equal(TranscriptFollowMode.FollowingBottom, sut.State.Mode);
        Assert.Null(sut.State.PinnedItemKey);
    }
}
