using SalmonEgg.Presentation.Utilities;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class TranscriptViewportCoordinatorTests
{
    [Fact]
    public void SessionActivated_WithRealConversationIdButNoMessages_KeepsConversationContextAuthoritative()
    {
        var sut = new TranscriptViewportCoordinator();

        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 1, TranscriptViewportActivationKind.ColdEnter));

        var command = sut.Handle(new TranscriptViewportEvent.ViewportFactChanged(
            "conv-a",
            1,
            new TranscriptViewportFact(
                HasItems: false,
                IsReady: false,
                IsAtBottom: true,
                IsProgrammaticScrollInFlight: false)));

        Assert.Equal(TranscriptViewportState.Idle, sut.State);
        Assert.NotEqual("StaleOrMismatchedContext", command.Reason);
        Assert.Equal(TranscriptViewportState.Idle, sut.GetConversationState("conv-a")?.Mode);
    }

    [Fact]
    public void WarmReturn_DetachedConversationWithPendingToken_WaitsForProjectionReady()
    {
        var sut = new TranscriptViewportCoordinator();
        var token = new TranscriptProjectionRestoreToken(
            ConversationId: "conv-a",
            ProjectionEpoch: 7,
            ProjectionItemKey: "msg:agent-0042");

        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 1, TranscriptViewportActivationKind.ColdEnter));
        sut.Handle(new TranscriptViewportEvent.UserDetached("conv-a", 1, token));

        var command = sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 2, TranscriptViewportActivationKind.WarmReturn));

        Assert.Equal(TranscriptViewportCommandKind.None, command.Kind);
        Assert.Equal(TranscriptViewportState.DetachedPendingRestore, sut.State);
        Assert.False(sut.IsAutoFollowAttached);
        Assert.Equal(TranscriptViewportState.DetachedPendingRestore, sut.GetConversationState("conv-a")?.Mode);
        Assert.Equal(token, sut.GetConversationState("conv-a")?.RestoreToken);
    }

    [Fact]
    public void ProjectionReady_DispatchesRequestRestoreForPendingDetachedConversation()
    {
        var sut = new TranscriptViewportCoordinator();
        var token = new TranscriptProjectionRestoreToken("conv-a", 7, "msg:agent-0042");

        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 1, TranscriptViewportActivationKind.ColdEnter));
        sut.Handle(new TranscriptViewportEvent.UserDetached("conv-a", 1, token));
        var warmReturn = sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 2, TranscriptViewportActivationKind.WarmReturn));
        Assert.Equal(TranscriptViewportCommandKind.None, warmReturn.Kind);
        Assert.Equal(TranscriptViewportState.DetachedPendingRestore, sut.State);

        var command = sut.Handle(new TranscriptViewportEvent.ProjectionReady("conv-a", 2, 7));

        Assert.Equal(TranscriptViewportCommandKind.RequestRestore, command.Kind);
        Assert.Equal(token, command.RestoreToken);
        Assert.Equal(TranscriptViewportState.DetachedRestoring, sut.State);
        Assert.False(sut.IsAutoFollowAttached);
        Assert.Equal(nameof(TranscriptViewportEvent.ProjectionReady), command.Transition?.EventName);
        Assert.Equal(7, sut.GetConversationState("conv-a")?.PendingProjectionEpoch);
    }

    [Fact]
    public void RestoreConfirmed_ReturnsConversationToDetachedStateAfterRestore()
    {
        var sut = new TranscriptViewportCoordinator();
        var token = new TranscriptProjectionRestoreToken("conv-a", 7, "msg:agent-0042");

        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 1, TranscriptViewportActivationKind.ColdEnter));
        sut.Handle(new TranscriptViewportEvent.UserDetached("conv-a", 1, token));
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 2, TranscriptViewportActivationKind.WarmReturn));
        sut.Handle(new TranscriptViewportEvent.ProjectionReady("conv-a", 2, 7));

        var command = sut.Handle(new TranscriptViewportEvent.RestoreConfirmed("conv-a", 2, token));

        Assert.Equal(TranscriptViewportCommandKind.None, command.Kind);
        Assert.Equal(TranscriptViewportState.DetachedByUser, sut.State);
        Assert.False(sut.IsAutoFollowAttached);
        Assert.Equal(nameof(TranscriptViewportEvent.RestoreConfirmed), sut.LastTransition?.EventName);
        Assert.Null(sut.GetConversationState("conv-a")?.PendingProjectionEpoch);
    }

    [Fact]
    public void RestoreUnavailable_LeavesConversationDetachedWithoutBottomRecovery()
    {
        var sut = new TranscriptViewportCoordinator();
        var token = new TranscriptProjectionRestoreToken("conv-a", 7, "msg:agent-0042");

        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 1, TranscriptViewportActivationKind.ColdEnter));
        sut.Handle(new TranscriptViewportEvent.UserDetached("conv-a", 1, token));
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 2, TranscriptViewportActivationKind.WarmReturn));
        sut.Handle(new TranscriptViewportEvent.ProjectionReady("conv-a", 2, 7));

        var command = sut.Handle(new TranscriptViewportEvent.RestoreUnavailable("conv-a", 2, "ItemNotMaterialized"));

        Assert.Equal(TranscriptViewportCommandKind.None, command.Kind);
        Assert.Equal(TranscriptViewportState.DetachedByUser, sut.State);
        Assert.False(sut.IsAutoFollowAttached);
        Assert.Equal(nameof(TranscriptViewportEvent.RestoreUnavailable), sut.LastTransition?.EventName);
        Assert.Null(sut.GetConversationState("conv-a")?.PendingProjectionEpoch);
    }

    [Fact]
    public void RestoreAbandoned_LeavesConversationDetachedWithoutBottomRecovery()
    {
        var sut = new TranscriptViewportCoordinator();
        var token = new TranscriptProjectionRestoreToken("conv-a", 7, "msg:agent-0042");

        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 1, TranscriptViewportActivationKind.ColdEnter));
        sut.Handle(new TranscriptViewportEvent.UserDetached("conv-a", 1, token));
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 2, TranscriptViewportActivationKind.WarmReturn));
        sut.Handle(new TranscriptViewportEvent.ProjectionReady("conv-a", 2, 7));

        var command = sut.Handle(new TranscriptViewportEvent.RestoreAbandoned("conv-a", 2, "UserInterrupted"));

        Assert.Equal(TranscriptViewportCommandKind.None, command.Kind);
        Assert.Equal(TranscriptViewportState.DetachedByUser, sut.State);
        Assert.False(sut.IsAutoFollowAttached);
        Assert.Equal(nameof(TranscriptViewportEvent.RestoreAbandoned), sut.LastTransition?.EventName);
        Assert.Null(sut.GetConversationState("conv-a")?.PendingProjectionEpoch);
    }

    [Fact]
    public void ProjectionReady_WithStaleGeneration_IsIgnoredAndPreservesPendingRestore()
    {
        var sut = new TranscriptViewportCoordinator();
        var token = new TranscriptProjectionRestoreToken("conv-a", 7, "msg:agent-0042");

        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 1, TranscriptViewportActivationKind.ColdEnter));
        sut.Handle(new TranscriptViewportEvent.UserDetached("conv-a", 1, token));
        sut.Handle(new TranscriptViewportEvent.SessionActivated("conv-a", 2, TranscriptViewportActivationKind.WarmReturn));

        var command = sut.Handle(new TranscriptViewportEvent.ProjectionReady("conv-a", 1, 7));

        Assert.Equal(TranscriptViewportCommandKind.None, command.Kind);
        Assert.Equal("StaleOrMismatchedContext", command.Reason);
        Assert.Equal(TranscriptViewportState.DetachedPendingRestore, sut.State);
        Assert.Equal(TranscriptViewportState.DetachedPendingRestore, sut.GetConversationState("conv-a")?.Mode);
    }
}
