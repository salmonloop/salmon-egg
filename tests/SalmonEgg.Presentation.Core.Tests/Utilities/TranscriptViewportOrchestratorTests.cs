using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class TranscriptViewportOrchestratorTests
{
    [Fact]
    public void ReadyButNotBottomDuringProgrammaticSettle_KeepsAutoFollowAttachedAndReissuesThroughOrchestrator()
    {
        var sut = new TranscriptViewportOrchestrator();
        sut.Activate("conv-1", TranscriptViewportActivationKind.ColdEnter);
        sut.BeginSettleRound("conv-1");

        var initial = sut.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);
        var observed = sut.ReportSettled("conv-1", initial.Generation, TranscriptScrollSettleObservation.ReadyButNotAtBottom);
        var retry = sut.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);

        Assert.Equal(TranscriptScrollAction.IssueScrollRequest, initial.Action);
        Assert.Equal(TranscriptScrollAction.None, observed.Action);
        Assert.Equal(TranscriptScrollAction.IssueScrollRequest, retry.Action);
        Assert.True(sut.Snapshot.IsAutoFollowAttached);
        Assert.False(sut.Snapshot.IsViewportDetached);
    }

    [Fact]
    public void UserIntentDuringSettle_AbortsSettleAndMarksDetached()
    {
        var sut = new TranscriptViewportOrchestrator();
        sut.Activate("conv-1", TranscriptViewportActivationKind.ColdEnter);
        sut.BeginSettleRound("conv-1");
        _ = sut.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);

        var aborted = sut.AbortSettleForUserInteraction();
        var detach = sut.Handle(sut.CreateUserIntentScrollEvent("conv-1"));

        Assert.Equal(TranscriptScrollAction.Aborted, aborted.Action);
        Assert.Equal(TranscriptViewportCommandKind.MarkAutoFollowDetached, detach.Kind);
        Assert.True(sut.Snapshot.IsViewportDetached);
        Assert.False(sut.Snapshot.IsAutoFollowAttached);
    }

    [Fact]
    public void ObserveViewportFact_UserScrollAwayFromBottom_DetachesWithCapturedRestoreToken()
    {
        var sut = new TranscriptViewportOrchestrator();
        var token = new TranscriptProjectionRestoreToken("conv-1", 42, "item-7");
        sut.Activate("conv-1", TranscriptViewportActivationKind.ColdEnter);
        sut.MarkUserScrollIntentStarted();

        var result = sut.ObserveViewportFact(
            "conv-1",
            new TranscriptViewportFact(
                HasItems: true,
                IsReady: true,
                IsAtBottom: false,
                IsProgrammaticScrollInFlight: false),
            token);

        Assert.Equal(TranscriptViewportCommandKind.MarkAutoFollowDetached, result.Command.Kind);
        Assert.True(sut.Snapshot.IsViewportDetached);
        Assert.Equal(token, sut.GetConversationState("conv-1")?.RestoreToken);
    }

    [Fact]
    public void ObserveViewportFact_DetachedAttachIntentAtBottom_AttachesThroughOrchestrator()
    {
        var sut = new TranscriptViewportOrchestrator();
        sut.Activate("conv-1", TranscriptViewportActivationKind.ColdEnter);
        sut.Handle(sut.CreateUserIntentScrollEvent("conv-1"));
        sut.MarkAttachToBottomIntent();
        sut.MarkUserScrollIntentCompleted();

        var result = sut.ObserveViewportFact(
            "conv-1",
            new TranscriptViewportFact(
                HasItems: true,
                IsReady: true,
                IsAtBottom: true,
                IsProgrammaticScrollInFlight: false),
            restoreToken: null);

        Assert.Equal(TranscriptViewportCommandKind.MarkAutoFollowAttached, result.Command.Kind);
        Assert.False(sut.Snapshot.IsViewportDetached);
        Assert.True(sut.Snapshot.IsAutoFollowAttached);
    }

    [Fact]
    public void ObserveViewportFact_UserScrollDuringProgrammaticSettle_RequestsStopAndDetaches()
    {
        var sut = new TranscriptViewportOrchestrator();
        sut.Activate("conv-1", TranscriptViewportActivationKind.ColdEnter);
        sut.BeginSettleRound("conv-1");
        _ = sut.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);
        sut.MarkUserScrollIntentStarted();

        var result = sut.ObserveViewportFact(
            "conv-1",
            new TranscriptViewportFact(
                HasItems: true,
                IsReady: true,
                IsAtBottom: false,
                IsProgrammaticScrollInFlight: true),
            restoreToken: null);

        Assert.Equal(TranscriptViewportCommandKind.MarkAutoFollowDetached, result.Command.Kind);
        Assert.True(sut.Snapshot.IsViewportDetached);
        Assert.False(sut.HasPendingSettle);
        Assert.False(sut.IsProgrammaticScrollInFlight);
    }

    [Fact]
    public void ObserveViewportFact_DetachedPendingRestore_UserIntentDoesNotReattachOrReclaimBottom()
    {
        var sut = new TranscriptViewportOrchestrator();
        var token = new TranscriptProjectionRestoreToken("conv-a", 7, "item-9");
        sut.Activate("conv-a", TranscriptViewportActivationKind.ColdEnter);
        sut.Handle(sut.CreateUserDetachedEvent("conv-a", token));
        sut.Activate("conv-a", TranscriptViewportActivationKind.WarmReturn);

        var result = sut.ObserveViewportFact(
            "conv-a",
            new TranscriptViewportFact(
                HasItems: true,
                IsReady: true,
                IsAtBottom: false,
                IsProgrammaticScrollInFlight: false),
            token);

        Assert.NotEqual(TranscriptViewportCommandKind.MarkAutoFollowAttached, result.Command.Kind);
        Assert.False(sut.IsAutoFollowAttached);
    }

    [Fact]
    public void ResetForConversationChange_PreventsOldSettleObservationFromMutatingActiveGeneration()
    {
        var sut = new TranscriptViewportOrchestrator();
        sut.Activate("conv-1", TranscriptViewportActivationKind.ColdEnter);
        sut.BeginSettleRound("conv-1");
        var initial = sut.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);

        sut.ResetForConversationChange();
        sut.Activate("conv-2", TranscriptViewportActivationKind.ColdEnter);
        sut.BeginSettleRound("conv-2");
        var ignored = sut.ReportSettled("conv-1", initial.Generation, TranscriptScrollSettleObservation.AtBottom);
        var next = sut.TryIssueScrollRequest("conv-2", hasMessages: true, isReady: true);

        Assert.Equal(TranscriptScrollAction.None, ignored.Action);
        Assert.Equal(TranscriptScrollAction.IssueScrollRequest, next.Action);
        Assert.Equal(next.Generation, sut.Snapshot.ActiveScrollGeneration);
    }

    [Fact]
    public void ScrollRequestTokens_AreOpaqueAndInvalidatedByConversationReset()
    {
        var sut = new TranscriptViewportOrchestrator();
        sut.Activate("conv-1", TranscriptViewportActivationKind.ColdEnter);
        sut.BeginSettleRound("conv-1");
        _ = sut.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);

        Assert.True(sut.TryCaptureActiveScrollRequestToken("conv-1", out var token));
        Assert.True(sut.MatchesActiveScrollRequest(token, "conv-1"));

        sut.ResetForConversationChange();

        Assert.False(sut.MatchesActiveScrollRequest(token, "conv-1"));
    }

    [Fact]
    public void ScrollToBottomScheduleTokens_AreOpaqueAndInvalidatedByInteractionReset()
    {
        var sut = new TranscriptViewportOrchestrator();
        sut.Activate("conv-1", TranscriptViewportActivationKind.ColdEnter);

        Assert.True(sut.TryBeginScrollToBottomSchedule("conv-1", out var token));
        Assert.True(sut.CanExecuteScrollToBottomSchedule(token, "conv-1"));

        sut.StopInitialScrollForManualInteraction();

        Assert.False(sut.CanExecuteScrollToBottomSchedule(token, "conv-1"));
    }
}
