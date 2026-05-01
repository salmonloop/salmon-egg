using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class TranscriptAutoFollowControllerTests
{
    [Fact]
    public void RegisterManualViewportIntent_DisablesAutoFollowUntilViewportSettles()
    {
        var controller = new TranscriptAutoFollowController();

        controller.RegisterManualViewportIntent(hasMessages: true);

        Assert.False(controller.IsAutoFollowEnabled);
        Assert.True(controller.HasPendingManualViewportEvaluation);
    }

    [Fact]
    public void ResolveManualViewportState_DoesNotPrematurelyReEnableAutoFollow_WhileViewportIsStillAtBottom()
    {
        var controller = new TranscriptAutoFollowController();
        controller.RegisterManualViewportIntent(hasMessages: true);

        var resolved = controller.ResolveManualViewportState(isViewportAtBottom: true);

        Assert.False(resolved);
        Assert.False(controller.IsAutoFollowEnabled);
        Assert.True(controller.HasPendingManualViewportEvaluation);
    }

    [Fact]
    public void ResolveManualViewportState_KeepsAutoFollowDisabled_WhenViewportRemainsDetached()
    {
        var controller = new TranscriptAutoFollowController();
        controller.RegisterManualViewportIntent(hasMessages: true);

        var resolved = controller.ResolveManualViewportState(isViewportAtBottom: false);

        Assert.True(resolved);
        Assert.False(controller.IsAutoFollowEnabled);
        Assert.False(controller.HasPendingManualViewportEvaluation);
    }

    [Fact]
    public void ResolveManualViewportState_ReEnablesAutoFollow_WhenUserReturnsToBottomAfterDetaching()
    {
        var controller = new TranscriptAutoFollowController();
        controller.RegisterManualViewportIntent(hasMessages: true);
        controller.ResolveManualViewportState(isViewportAtBottom: false);

        var resolved = controller.ResolveManualViewportState(isViewportAtBottom: true);

        Assert.True(resolved);
        Assert.True(controller.IsAutoFollowEnabled);
        Assert.False(controller.HasPendingManualViewportEvaluation);
    }

    [Fact]
    public void ShouldRecoverBottom_ReturnsTrue_WhenAutoFollowRemainsEnabledAndViewportDrifts()
    {
        var controller = new TranscriptAutoFollowController();

        var shouldRecover = controller.ShouldRecoverBottom(
            isSessionActive: true,
            hasMessages: true,
            hasPendingInitialScroll: false,
            isProgrammaticScrollInFlight: false,
            isViewportAtBottom: false);

        Assert.True(shouldRecover);
    }

    [Fact]
    public void ShouldRecoverBottom_ReturnsFalse_WhenUserDetachedFromBottom()
    {
        var controller = new TranscriptAutoFollowController();
        controller.RegisterManualViewportIntent(hasMessages: true);
        controller.ResolveManualViewportState(isViewportAtBottom: false);

        var shouldRecover = controller.ShouldRecoverBottom(
            isSessionActive: true,
            hasMessages: true,
            hasPendingInitialScroll: false,
            isProgrammaticScrollInFlight: false,
            isViewportAtBottom: false);

        Assert.False(shouldRecover);
    }

    [Fact]
    public void Reset_RestoresAutoFollowForNewConversation()
    {
        var controller = new TranscriptAutoFollowController();
        controller.RegisterManualViewportIntent(hasMessages: true);
        controller.ResolveManualViewportState(isViewportAtBottom: false);

        controller.Reset();

        Assert.True(controller.IsAutoFollowEnabled);
        Assert.False(controller.HasPendingManualViewportEvaluation);
    }
}
