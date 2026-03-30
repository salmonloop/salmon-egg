using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class SelectionProjectionApplyGateTests
{
    [Fact]
    public void RequestApply_WhenNoInteractionInFlight_AppliesImmediately()
    {
        var gate = new SelectionProjectionApplyGate();

        var decision = gate.RequestApply();

        Assert.Equal(SelectionProjectionApplyDecision.ApplyNow, decision);
    }

    [Fact]
    public void RequestApply_WhenInteractionIsInFlight_DefersUntilInteractionEnds()
    {
        var gate = new SelectionProjectionApplyGate();
        gate.BeginInteraction();

        var firstDecision = gate.RequestApply();
        var secondDecision = gate.RequestApply();
        var shouldApplyAfterInteraction = gate.EndInteraction();

        Assert.Equal(SelectionProjectionApplyDecision.Defer, firstDecision);
        Assert.Equal(SelectionProjectionApplyDecision.Defer, secondDecision);
        Assert.True(shouldApplyAfterInteraction);
    }

    [Fact]
    public void EndInteraction_WithoutDeferredApply_DoesNotRequestReplay()
    {
        var gate = new SelectionProjectionApplyGate();
        gate.BeginInteraction();

        var shouldApplyAfterInteraction = gate.EndInteraction();

        Assert.False(shouldApplyAfterInteraction);
    }

    [Fact]
    public void EndInteraction_WhenNestedInteractionsRemain_DoesNotReleaseDeferredApplyUntilLastInteractionEnds()
    {
        var gate = new SelectionProjectionApplyGate();
        gate.BeginInteraction();
        gate.RequestApply();
        gate.BeginInteraction();
        gate.RequestApply();

        var shouldApplyAfterInnerInteraction = gate.EndInteraction();
        var shouldApplyAfterOuterInteraction = gate.EndInteraction();

        Assert.False(shouldApplyAfterInnerInteraction);
        Assert.True(shouldApplyAfterOuterInteraction);
    }

    [Fact]
    public void TryScheduleDeferredApply_CoalescesUntilReleased()
    {
        var gate = new SelectionProjectionApplyGate();

        var firstAttempt = gate.TryScheduleDeferredApply();
        var secondAttempt = gate.TryScheduleDeferredApply();
        gate.ReleaseScheduledDeferredApply();
        var thirdAttempt = gate.TryScheduleDeferredApply();

        Assert.True(firstAttempt);
        Assert.False(secondAttempt);
        Assert.True(thirdAttempt);
    }
}
