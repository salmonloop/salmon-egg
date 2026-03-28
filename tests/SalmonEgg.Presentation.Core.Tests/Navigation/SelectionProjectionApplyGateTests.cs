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
}
