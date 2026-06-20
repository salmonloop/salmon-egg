using SalmonEgg.Presentation.ViewModels.Start;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Start;

public sealed class StartSessionModePolicyTests
{
    [Fact]
    public void Compute_WhenDraftIsNotReady_DisablesVisibleSelector()
    {
        var snapshot = StartSessionModePolicy.Compute(new StartSessionModeState(
            IsStarting: false,
            IsConnectionReady: false,
            IsConnectionInProgress: false,
            IsDraftRefreshPending: false,
            IsDraftLoading: false,
            IsDraftReady: false,
            ModeCount: 0));

        Assert.Equal(StartSessionModeStage.Unavailable, snapshot.Stage);
        Assert.False(snapshot.IsEnabled);
        Assert.False(snapshot.CanSubmitPrompt);
    }

    [Fact]
    public void Compute_WhenRealModesAreLoading_ShowsDisabledSelector()
    {
        var snapshot = StartSessionModePolicy.Compute(new StartSessionModeState(
            IsStarting: false,
            IsConnectionReady: false,
            IsConnectionInProgress: false,
            IsDraftRefreshPending: true,
            IsDraftLoading: false,
            IsDraftReady: false,
            ModeCount: 0));

        Assert.Equal(StartSessionModeStage.Loading, snapshot.Stage);
        Assert.False(snapshot.IsEnabled);
        Assert.False(snapshot.CanSubmitPrompt);
    }

    [Fact]
    public void Compute_WhenConnectionIsStillInProgress_ShowsLoadingSelector()
    {
        var snapshot = StartSessionModePolicy.Compute(new StartSessionModeState(
            IsStarting: false,
            IsConnectionReady: false,
            IsConnectionInProgress: true,
            IsDraftRefreshPending: false,
            IsDraftLoading: false,
            IsDraftReady: false,
            ModeCount: 0));

        Assert.Equal(StartSessionModeStage.Loading, snapshot.Stage);
        Assert.False(snapshot.IsEnabled);
        Assert.False(snapshot.CanSubmitPrompt);
    }

    [Fact]
    public void Compute_WhenReadyWithModes_EnablesSelector()
    {
        var snapshot = StartSessionModePolicy.Compute(new StartSessionModeState(
            IsStarting: false,
            IsConnectionReady: true,
            IsConnectionInProgress: false,
            IsDraftRefreshPending: false,
            IsDraftLoading: false,
            IsDraftReady: true,
            ModeCount: 2));

        Assert.Equal(StartSessionModeStage.Ready, snapshot.Stage);
        Assert.True(snapshot.IsEnabled);
        Assert.True(snapshot.CanSubmitPrompt);
    }

    [Fact]
    public void Compute_WhenRefreshStartsAfterExistingModes_DisablesStaleSelector()
    {
        var snapshot = StartSessionModePolicy.Compute(new StartSessionModeState(
            IsStarting: false,
            IsConnectionReady: true,
            IsConnectionInProgress: false,
            IsDraftRefreshPending: true,
            IsDraftLoading: false,
            IsDraftReady: true,
            ModeCount: 2));

        Assert.Equal(StartSessionModeStage.Loading, snapshot.Stage);
        Assert.False(snapshot.IsEnabled);
        Assert.False(snapshot.CanSubmitPrompt);
    }

    [Fact]
    public void Compute_WhenConnectionIsNotReadyWithProjectedModes_DisablesSelector()
    {
        var snapshot = StartSessionModePolicy.Compute(new StartSessionModeState(
            IsStarting: false,
            IsConnectionReady: false,
            IsConnectionInProgress: false,
            IsDraftRefreshPending: false,
            IsDraftLoading: false,
            IsDraftReady: true,
            ModeCount: 2));

        Assert.Equal(StartSessionModeStage.Unavailable, snapshot.Stage);
        Assert.False(snapshot.IsEnabled);
        Assert.False(snapshot.CanSubmitPrompt);
    }
}
