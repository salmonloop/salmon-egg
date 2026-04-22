using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class TranscriptScrollSettlerTests
{
    [Fact]
    public void TryIssueScrollRequest_DoesNotConsumeBudget_WhenViewIsNotReady()
    {
        var settler = new TranscriptScrollSettler(maxReadyButNotBottomFailures: 1);
        settler.BeginRound("conv-1");
        var generation = settler.Generation;

        var notReady = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: false);
        var ready = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);

        Assert.Equal(TranscriptScrollAction.None, notReady.Action);
        Assert.Equal(TranscriptScrollAction.IssueScrollRequest, ready.Action);
        Assert.Equal(generation, ready.Generation);
    }

    [Fact]
    public void ReportSettled_ConsumesBudget_WhenReadyButNotAtBottom()
    {
        var settler = new TranscriptScrollSettler(maxReadyButNotBottomFailures: 2);
        settler.BeginRound("conv-1");

        var firstIssue = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);
        var firstFailure = settler.ReportSettled("conv-1", firstIssue.Generation, TranscriptScrollSettleObservation.ReadyButNotAtBottom);
        var secondIssue = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);
        var secondFailure = settler.ReportSettled("conv-1", secondIssue.Generation, TranscriptScrollSettleObservation.ReadyButNotAtBottom);
        var exhausted = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);

        Assert.Equal(TranscriptScrollAction.None, firstFailure.Action);
        Assert.Equal(TranscriptScrollAction.None, secondFailure.Action);
        Assert.Equal(TranscriptScrollAction.Exhausted, exhausted.Action);
    }

    [Fact]
    public void ReportSettled_CompletesRound_WhenBottomReached()
    {
        var settler = new TranscriptScrollSettler();
        settler.BeginRound("conv-1");

        var issued = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);
        var completed = settler.ReportSettled("conv-1", issued.Generation, TranscriptScrollSettleObservation.AtBottom);
        var afterComplete = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);

        Assert.Equal(TranscriptScrollAction.Completed, completed.Action);
        Assert.Equal(TranscriptScrollAction.None, afterComplete.Action);
        Assert.False(settler.HasPendingWork);
    }

    [Fact]
    public void AbortForUserInteraction_StopsPendingRound()
    {
        var settler = new TranscriptScrollSettler();
        settler.BeginRound("conv-1");

        var aborted = settler.AbortForUserInteraction();
        var afterAbort = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);

        Assert.Equal(TranscriptScrollAction.Aborted, aborted.Action);
        Assert.Equal(TranscriptScrollAction.None, afterAbort.Action);
        Assert.False(settler.HasPendingWork);
    }

    [Fact]
    public void ReportSettled_IgnoresStaleGeneration()
    {
        var settler = new TranscriptScrollSettler();
        settler.BeginRound("conv-1");
        var staleIssue = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);

        settler.BeginRound("conv-1");
        var ignored = settler.ReportSettled("conv-1", staleIssue.Generation, TranscriptScrollSettleObservation.AtBottom);
        var currentIssue = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);

        Assert.Equal(TranscriptScrollAction.None, ignored.Action);
        Assert.Equal(TranscriptScrollAction.IssueScrollRequest, currentIssue.Action);
        Assert.True(currentIssue.Generation > staleIssue.Generation);
    }

    [Fact]
    public void TryIssueScrollRequest_ReturnsExhausted_WhenFailureBudgetReached()
    {
        var settler = new TranscriptScrollSettler(maxReadyButNotBottomFailures: 1);
        settler.BeginRound("conv-1");

        var issued = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);
        settler.ReportSettled("conv-1", issued.Generation, TranscriptScrollSettleObservation.ReadyButNotAtBottom);
        var exhausted = settler.TryIssueScrollRequest("conv-1", hasMessages: true, isReady: true);

        Assert.Equal(TranscriptScrollAction.Exhausted, exhausted.Action);
        Assert.False(settler.HasPendingWork);
    }
}
