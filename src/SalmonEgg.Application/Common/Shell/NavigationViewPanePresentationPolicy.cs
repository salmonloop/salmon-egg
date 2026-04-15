namespace SalmonEgg.Application.Common.Shell;

public enum NavigationViewPanePresentationMode
{
    Expanded,
    Compact,
    Minimal
}

public struct NavigationViewPanePresentationState
{
    public bool HasConfirmedOverlayPaneOpen { get; }

    public static NavigationViewPanePresentationState Default => new NavigationViewPanePresentationState(false);

    public NavigationViewPanePresentationState(bool hasConfirmedOverlayPaneOpen)
    {
        HasConfirmedOverlayPaneOpen = hasConfirmedOverlayPaneOpen;
    }
}

public struct NavigationViewPanePresentationDecision
{
    public NavigationViewPanePresentationState NextState { get; }
    public bool ShouldReportPaneOpenIntent { get; }
    public bool ShouldApplyPaneProjection { get; }

    public NavigationViewPanePresentationDecision(
        NavigationViewPanePresentationState nextState,
        bool shouldReportPaneOpenIntent,
        bool shouldApplyPaneProjection)
    {
        NextState = nextState;
        ShouldReportPaneOpenIntent = shouldReportPaneOpenIntent;
        ShouldApplyPaneProjection = shouldApplyPaneProjection;
    }
}

public static class NavigationViewPanePresentationPolicy
{
    public static NavigationViewPanePresentationDecision Evaluate(
        NavigationViewPanePresentationState state,
        bool isPaneOpen,
        bool isDisplayModeChanged,
        NavigationViewPanePresentationMode displayMode,
        bool desiredPaneOpen)
    {
        var nextState = state;
        var shouldReportPaneOpenIntent = false;
        var shouldApplyPaneProjection = false;
        var hasStoreDrift = desiredPaneOpen != isPaneOpen;
        var isExpandedMode = displayMode == NavigationViewPanePresentationMode.Expanded;

        if (isExpandedMode && hasStoreDrift)
        {
            shouldApplyPaneProjection = true;
        }

        if (isExpandedMode)
        {
            nextState = new NavigationViewPanePresentationState(false);
        }
        else
        {
            if (isPaneOpen)
            {
                nextState = new NavigationViewPanePresentationState(true);
            }

            if (hasStoreDrift)
            {
                if (!desiredPaneOpen && isPaneOpen)
                {
                    nextState = new NavigationViewPanePresentationState(true);
                }
                else if (desiredPaneOpen && !isPaneOpen && state.HasConfirmedOverlayPaneOpen)
                {
                    shouldReportPaneOpenIntent = true;
                    nextState = new NavigationViewPanePresentationState(false);
                }
            }
        }

        return new NavigationViewPanePresentationDecision(
            nextState,
            shouldReportPaneOpenIntent,
            shouldApplyPaneProjection);
    }
}
