using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Tests.ShellLayout;

public sealed class AuxiliaryPanelAnimationCoordinatorTests
{
    [Fact]
    public void CompleteAnimation_OpenCloseOpenWhileOpening_KeepsLatestOpenIntent()
    {
        var coordinator = new AuxiliaryPanelAnimationCoordinator();

        var open = coordinator.UpdateTarget(targetVisible: true, extent: 280, animationsEnabled: true);
        var closeWhileOpening = coordinator.UpdateTarget(targetVisible: false, extent: 0, animationsEnabled: true);
        var reopenWhileOpening = coordinator.UpdateTarget(targetVisible: true, extent: 280, animationsEnabled: true);
        var completion = coordinator.CompleteAnimation();

        Assert.Equal(AuxiliaryPanelAnimationAction.StartOpening, open.Action);
        Assert.Equal(280, open.TravelDistance);
        Assert.Equal(AuxiliaryPanelAnimationAction.None, closeWhileOpening.Action);
        Assert.Equal(AuxiliaryPanelAnimationAction.None, reopenWhileOpening.Action);
        Assert.Equal(AuxiliaryPanelAnimationAction.None, completion.Action);
        Assert.Equal(AuxiliaryPanelAnimationPhase.Visible, coordinator.Phase);
        Assert.True(coordinator.TargetVisible);
    }

    [Fact]
    public void CompleteAnimation_CloseRequestedWhileOpening_UsesLastVisibleExtentForClosing()
    {
        var coordinator = new AuxiliaryPanelAnimationCoordinator();

        _ = coordinator.UpdateTarget(targetVisible: true, extent: 320, animationsEnabled: true);
        _ = coordinator.UpdateTarget(targetVisible: false, extent: 0, animationsEnabled: true);

        var completion = coordinator.CompleteAnimation();

        Assert.Equal(AuxiliaryPanelAnimationAction.StartClosing, completion.Action);
        Assert.Equal(320, completion.TravelDistance);
        Assert.Equal(AuxiliaryPanelAnimationPhase.Closing, coordinator.Phase);
        Assert.False(coordinator.TargetVisible);
    }

    [Fact]
    public void CompleteAnimation_ReopenRequestedWhileClosing_UsesLastVisibleExtentForOpening()
    {
        var coordinator = new AuxiliaryPanelAnimationCoordinator(initiallyVisible: true, initialExtent: 260);

        var close = coordinator.UpdateTarget(targetVisible: false, extent: 0, animationsEnabled: true);
        var reopenWhileClosing = coordinator.UpdateTarget(targetVisible: true, extent: 0, animationsEnabled: true);
        var completion = coordinator.CompleteAnimation();

        Assert.Equal(AuxiliaryPanelAnimationAction.StartClosing, close.Action);
        Assert.Equal(260, close.TravelDistance);
        Assert.Equal(AuxiliaryPanelAnimationAction.None, reopenWhileClosing.Action);
        Assert.Equal(AuxiliaryPanelAnimationAction.StartOpening, completion.Action);
        Assert.Equal(260, completion.TravelDistance);
        Assert.Equal(AuxiliaryPanelAnimationPhase.Opening, coordinator.Phase);
        Assert.True(coordinator.TargetVisible);
    }

    [Fact]
    public void UpdateTarget_AnimationsDisabled_SnapsImmediatelyToVisibleState()
    {
        var coordinator = new AuxiliaryPanelAnimationCoordinator();

        var request = coordinator.UpdateTarget(targetVisible: true, extent: 180, animationsEnabled: false);

        Assert.Equal(AuxiliaryPanelAnimationAction.Show, request.Action);
        Assert.Equal(AuxiliaryPanelAnimationPhase.Visible, coordinator.Phase);
        Assert.True(coordinator.TargetVisible);
    }

    [Fact]
    public void SnapTo_HiddenThenShowWithoutExtent_FallsBackToHide()
    {
        var coordinator = new AuxiliaryPanelAnimationCoordinator(initiallyVisible: true, initialExtent: 200);

        coordinator.SnapTo(visible: false, extent: 0);
        var request = coordinator.UpdateTarget(targetVisible: true, extent: 0, animationsEnabled: true);

        Assert.Equal(AuxiliaryPanelAnimationAction.StartOpening, request.Action);
        Assert.Equal(200, request.TravelDistance);
        Assert.Equal(AuxiliaryPanelAnimationPhase.Opening, coordinator.Phase);
        Assert.True(coordinator.TargetVisible);
    }
}
