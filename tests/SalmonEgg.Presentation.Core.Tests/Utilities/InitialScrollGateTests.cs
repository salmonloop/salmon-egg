using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class InitialScrollGateTests
{
    [Fact]
    public void TrySchedule_ReturnsFalse_WhenNoItems()
    {
        var gate = new InitialScrollGate();

        var scheduled = gate.TrySchedule(0);
        var scheduledAfterItems = gate.TrySchedule(1);

        Assert.False(scheduled);
        Assert.True(scheduledAfterItems);
    }

    [Fact]
    public void TryComplete_ReturnsFalse_WhenScrollDidNotReachBottom_AndKeepsPending()
    {
        var gate = new InitialScrollGate();

        var scheduled = gate.TrySchedule(1);
        var completed = gate.TryComplete(reachedBottom: false);
        var scheduledAgain = gate.TrySchedule(1);

        Assert.True(scheduled);
        Assert.False(completed);
        Assert.True(scheduledAgain);
        Assert.True(gate.HasPending);
    }

    [Fact]
    public void TrySchedule_ReturnsFalse_WhenAlreadyInFlight()
    {
        var gate = new InitialScrollGate();

        var first = gate.TrySchedule(1);
        var second = gate.TrySchedule(1);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void TryComplete_ReturnsFalse_WhenItemsCleared_AndKeepsPending()
    {
        var gate = new InitialScrollGate();

        var scheduled = gate.TrySchedule(1);
        gate.CancelInFlight();
        var completed = gate.TryComplete(reachedBottom: false);
        var scheduledAgain = gate.TrySchedule(1);

        Assert.True(scheduled);
        Assert.False(completed);
        Assert.True(scheduledAgain);
    }

    [Fact]
    public void TryComplete_ClearsPending_WhenItemsAvailable()
    {
        var gate = new InitialScrollGate();

        var scheduled = gate.TrySchedule(1);
        var completed = gate.TryComplete(reachedBottom: true);
        var scheduledAfterComplete = gate.TrySchedule(1);
        gate.MarkPending();
        var scheduledAfterReset = gate.TrySchedule(1);

        Assert.True(scheduled);
        Assert.True(completed);
        Assert.False(scheduledAfterComplete);
        Assert.True(scheduledAfterReset);
        Assert.True(gate.HasPending);
    }

    [Fact]
    public void MarkPending_PreservesInFlightUntilCurrentAttemptFinishes()
    {
        var gate = new InitialScrollGate();

        var scheduled = gate.TrySchedule(1);
        gate.MarkPending();
        var scheduledAgainBeforeCancel = gate.TrySchedule(1);
        gate.CancelInFlight();
        var scheduledAgainAfterCancel = gate.TrySchedule(1);

        Assert.True(scheduled);
        Assert.False(scheduledAgainBeforeCancel);
        Assert.True(scheduledAgainAfterCancel);
    }

    [Fact]
    public void ClearPending_StopsFurtherRetries_UntilReset()
    {
        var gate = new InitialScrollGate();

        gate.TrySchedule(1);
        gate.ClearPending();
        var scheduledWhileCleared = gate.TrySchedule(1);
        var hasPendingWhileCleared = gate.HasPending;
        gate.MarkPending();
        var scheduledAfterReset = gate.TrySchedule(1);

        Assert.False(hasPendingWhileCleared);
        Assert.False(scheduledWhileCleared);
        Assert.True(scheduledAfterReset);
    }

    [Fact]
    public void ClearPending_InvalidatesQueuedGeneration()
    {
        var gate = new InitialScrollGate();

        gate.MarkPending();
        var generationBeforeClear = gate.Generation;
        gate.ClearPending();

        Assert.True(gate.Generation > generationBeforeClear);
    }

    [Fact]
    public void MarkPending_InvalidatesQueuedGeneration()
    {
        var gate = new InitialScrollGate();

        var generationBeforeMark = gate.Generation;
        gate.MarkPending();

        Assert.True(gate.Generation > generationBeforeMark);
    }
}
