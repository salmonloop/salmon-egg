namespace SalmonEgg.Presentation.Core.Services;

public enum SelectionProjectionApplyDecision
{
    ApplyNow,
    Defer
}

public sealed class SelectionProjectionApplyGate
{
    private int _interactionDepth;
    private bool _hasDeferredApply;
    private bool _hasScheduledDeferredApply;

    public void BeginInteraction()
    {
        _interactionDepth++;
    }

    public SelectionProjectionApplyDecision RequestApply()
    {
        if (_interactionDepth <= 0)
        {
            return SelectionProjectionApplyDecision.ApplyNow;
        }

        _hasDeferredApply = true;
        return SelectionProjectionApplyDecision.Defer;
    }

    public bool TryScheduleDeferredApply()
    {
        if (_hasScheduledDeferredApply)
        {
            return false;
        }

        _hasScheduledDeferredApply = true;
        return true;
    }

    public void ReleaseScheduledDeferredApply()
    {
        _hasScheduledDeferredApply = false;
    }

    public bool EndInteraction()
    {
        if (_interactionDepth <= 0)
        {
            return false;
        }

        _interactionDepth--;
        if (_interactionDepth > 0)
        {
            return false;
        }

        var shouldApply = _hasDeferredApply;
        _hasDeferredApply = false;
        return shouldApply;
    }
}
