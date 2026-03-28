namespace SalmonEgg.Presentation.Core.Services;

public enum SelectionProjectionApplyDecision
{
    ApplyNow,
    Defer
}

public sealed class SelectionProjectionApplyGate
{
    private bool _isInteractionInFlight;
    private bool _hasDeferredApply;

    public void BeginInteraction()
    {
        _isInteractionInFlight = true;
        _hasDeferredApply = false;
    }

    public SelectionProjectionApplyDecision RequestApply()
    {
        if (!_isInteractionInFlight)
        {
            return SelectionProjectionApplyDecision.ApplyNow;
        }

        _hasDeferredApply = true;
        return SelectionProjectionApplyDecision.Defer;
    }

    public bool EndInteraction()
    {
        var shouldApply = _hasDeferredApply;
        _isInteractionInFlight = false;
        _hasDeferredApply = false;
        return shouldApply;
    }
}
