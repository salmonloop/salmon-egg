using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Navigation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

/// <summary>
/// Pins the retirement invariant of the shell activation fields. They describe one conversation
/// between them, so retiring that conversation must clear them as a set: a partial clear leaves the
/// runtime describing a conversation that no longer exists.
/// </summary>
public sealed class ShellNavigationRuntimeStateStoreRetirementTests
{
    [Fact]
    public void TryRetireSession_WhenSessionOwnsActivation_ClearsEveryActivationField()
    {
        var store = new ShellNavigationRuntimeStateStore
        {
            LatestActivationToken = 7,
            ActiveSessionActivationVersion = 7,
            CommittedSessionActivationVersion = 7,
            IsSessionActivationInProgress = true,
            DesiredSessionId = "conv-1",
            CommittedSessionId = "conv-1",
            ActiveSessionActivation = new SessionActivationSnapshot(
                "conv-1",
                "project-1",
                7,
                SessionActivationPhase.Selected)
        };

        var retired = store.TryRetireSession("conv-1");

        Assert.True(retired);
        Assert.Null(store.ActiveSessionActivation);
        Assert.Null(store.DesiredSessionId);
        Assert.Null(store.CommittedSessionId);
        // The committed version is the second half of the committed id and must not survive it.
        Assert.Equal(0, store.CommittedSessionActivationVersion);
        Assert.False(store.IsSessionActivationInProgress);
        Assert.Equal(0, store.ActiveSessionActivationVersion);
    }

    [Fact]
    public void TryRetireSession_WhenSessionOwnsNothing_LeavesStateUntouched()
    {
        var store = new ShellNavigationRuntimeStateStore
        {
            LatestActivationToken = 9,
            ActiveSessionActivationVersion = 9,
            CommittedSessionActivationVersion = 9,
            IsSessionActivationInProgress = true,
            DesiredSessionId = "conv-current",
            CommittedSessionId = "conv-current",
            ActiveSessionActivation = new SessionActivationSnapshot(
                "conv-current",
                "project-1",
                9,
                SessionActivationPhase.Selected)
        };

        var retired = store.TryRetireSession("conv-other");

        Assert.False(retired);
        Assert.Equal("conv-current", store.ActiveSessionActivation?.SessionId);
        Assert.Equal("conv-current", store.DesiredSessionId);
        Assert.Equal("conv-current", store.CommittedSessionId);
        Assert.Equal(9, store.CommittedSessionActivationVersion);
        Assert.True(store.IsSessionActivationInProgress);
        Assert.Equal(9, store.ActiveSessionActivationVersion);
    }

    [Fact]
    public void TryRetireSession_WhenOnlyTheCommittedSessionMatches_StillRetiresIt()
    {
        // A conversation can be committed while a newer activation is already in flight for another.
        var store = new ShellNavigationRuntimeStateStore
        {
            LatestActivationToken = 11,
            ActiveSessionActivationVersion = 11,
            CommittedSessionActivationVersion = 10,
            IsSessionActivationInProgress = true,
            DesiredSessionId = "conv-incoming",
            CommittedSessionId = "conv-outgoing",
            ActiveSessionActivation = new SessionActivationSnapshot(
                "conv-incoming",
                "project-1",
                11,
                SessionActivationPhase.Selected)
        };

        var retired = store.TryRetireSession("conv-outgoing");

        Assert.True(retired);
        Assert.Null(store.CommittedSessionId);
        Assert.Equal(0, store.CommittedSessionActivationVersion);
        // The unrelated in-flight activation keeps its own identity.
        Assert.Equal("conv-incoming", store.ActiveSessionActivation?.SessionId);
        Assert.Equal("conv-incoming", store.DesiredSessionId);
        // The progress flags belong to the in-flight activation and must not be cleared.
        Assert.True(store.IsSessionActivationInProgress);
        Assert.Equal(11, store.ActiveSessionActivationVersion);
    }

    [Fact]
    public void TryRetireSession_WithBlankSessionId_DoesNothing()
    {
        var store = new ShellNavigationRuntimeStateStore
        {
            CommittedSessionId = "conv-1",
            CommittedSessionActivationVersion = 3
        };

        Assert.False(store.TryRetireSession(" "));
        Assert.Equal("conv-1", store.CommittedSessionId);
        Assert.Equal(3, store.CommittedSessionActivationVersion);
    }
}
