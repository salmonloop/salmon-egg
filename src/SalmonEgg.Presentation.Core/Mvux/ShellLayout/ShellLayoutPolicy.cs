using System;

namespace SalmonEgg.Presentation.Core.Mvux.ShellLayout;

public static class ShellLayoutPolicy
{
    internal const double MinimumNavPaneWidth = 240;
    internal const double MaximumNavPaneWidth = 480;
    internal const double MinimumRightPanelWidth = 240;
    internal const double MaximumRightPanelWidth = 520;
    private const double BottomPanelMinHeight = 160;
    private const double BottomPanelMaxHeight = 360;
    private const double MinimumChatRegionHeight = 220;
    private const double MinimumDualPanelWidth = 1100;
    private const double MinimumDualPanelHeight = 700;

    public static ShellLayoutSnapshot Compute(ShellLayoutState state)
    {
        var availableWidth = state.WindowMetrics.EffectiveWidth > 0
            ? state.WindowMetrics.EffectiveWidth
            : state.WindowMetrics.Width;
        var availableHeight = state.WindowMetrics.EffectiveHeight > 0
            ? state.WindowMetrics.EffectiveHeight
            : state.WindowMetrics.Height;

        var mode = availableWidth >= 1000
            ? NavigationPaneDisplayMode.Expanded
            : availableWidth >= 640
                ? NavigationPaneDisplayMode.Compact
                : NavigationPaneDisplayMode.Minimal;
        var isOpen = mode switch
        {
            NavigationPaneDisplayMode.Expanded => state.UserNavOpenIntent != false,
            NavigationPaneDisplayMode.Compact => state.UserNavOpenIntent == true,
            NavigationPaneDisplayMode.Minimal => state.IsMinimalPaneOpen,
            _ => false
        };

        var searchVisible = mode != NavigationPaneDisplayMode.Minimal;
        var minSearch = mode == NavigationPaneDisplayMode.Expanded ? 220 : 180;
        var maxSearch = mode == NavigationPaneDisplayMode.Expanded ? 360 : 300;

        var navOpenPaneLength = ClampNavPaneWidth(state.NavOpenPaneLength, MinimumNavPaneWidth);
        var rightPanelPreferredWidth = ClampRightPanelWidth(state.RightPanelPreferredWidth, MinimumRightPanelWidth);
        var maxRightPanelWidth = Math.Min(MaximumRightPanelWidth, availableWidth);
        var contentHeight = Math.Max(0, availableHeight - state.TitleBarInsetsHeight);
        var maxBottomPanelHeight = Math.Min(BottomPanelMaxHeight, Math.Max(0, contentHeight - MinimumChatRegionHeight));
        var canToggleRightPanels = state.IsChatContext
            && state.HasRightPanelContent
            && maxRightPanelWidth >= MinimumRightPanelWidth
            && mode != NavigationPaneDisplayMode.Minimal;
        var canToggleBottomPanel = state.IsChatContext
            && state.SupportsLocalTerminal
            && maxBottomPanelHeight >= BottomPanelMinHeight;
        var showAuxiliaryTitleBarButtons = state.IsChatContext
            && (canToggleRightPanels || canToggleBottomPanel);
        var hasSearchRegion = searchVisible;
        var hasAuxiliaryRegion = showAuxiliaryTitleBarButtons;
        var titleBarInteractiveRegionToken = (hasSearchRegion ? 1 : 0) | (hasAuxiliaryRegion ? 2 : 0);

        var canShowSimultaneousAuxiliaryPanels =
            availableWidth >= MinimumDualPanelWidth && availableHeight >= MinimumDualPanelHeight;

        var rightPanelEligible = state.DesiredRightPanelMode != RightPanelMode.None
            && canToggleRightPanels;
        var bottomPanelEligible = state.DesiredBottomPanelMode != BottomPanelMode.None
            && canToggleBottomPanel;

        RightPanelMode effectiveRightPanelMode;
        BottomPanelMode effectiveBottomPanelMode;
        if (canShowSimultaneousAuxiliaryPanels)
        {
            effectiveRightPanelMode = rightPanelEligible ? state.DesiredRightPanelMode : RightPanelMode.None;
            effectiveBottomPanelMode = bottomPanelEligible ? state.DesiredBottomPanelMode : BottomPanelMode.None;
        }
        else
        {
            // Dual-unavailable: if both are eligible, pick the last-used area; otherwise fall back to the one that can render.
            if (rightPanelEligible && bottomPanelEligible)
            {
                if (state.LastAuxiliaryPanelArea == AuxiliaryPanelArea.Bottom)
                {
                    effectiveRightPanelMode = RightPanelMode.None;
                    effectiveBottomPanelMode = state.DesiredBottomPanelMode;
                }
                else
                {
                    effectiveRightPanelMode = state.DesiredRightPanelMode;
                    effectiveBottomPanelMode = BottomPanelMode.None;
                }
            }
            else if (rightPanelEligible)
            {
                effectiveRightPanelMode = state.DesiredRightPanelMode;
                effectiveBottomPanelMode = BottomPanelMode.None;
            }
            else if (bottomPanelEligible)
            {
                effectiveRightPanelMode = RightPanelMode.None;
                effectiveBottomPanelMode = state.DesiredBottomPanelMode;
            }
            else
            {
                effectiveRightPanelMode = RightPanelMode.None;
                effectiveBottomPanelMode = BottomPanelMode.None;
            }
        }

        var rightPaneCanRender = canToggleRightPanels;
        var rightPanelOpenPaneLength = rightPaneCanRender
            ? Math.Clamp(rightPanelPreferredWidth, MinimumRightPanelWidth, maxRightPanelWidth)
            : 0;
        var rightPanelVisible = effectiveRightPanelMode != RightPanelMode.None;
        double rightPanelWidth = 0;
        if (rightPanelVisible)
        {
            rightPanelWidth = rightPanelOpenPaneLength;
        }

        var bottomPanelVisible = effectiveBottomPanelMode != BottomPanelMode.None;
        double bottomPanelHeight = 0;
        if (bottomPanelVisible)
        {
            bottomPanelHeight = Math.Clamp(state.BottomPanelPreferredHeight, BottomPanelMinHeight, maxBottomPanelHeight);
        }

        return new ShellLayoutSnapshot(
            mode,
            isOpen,
            navOpenPaneLength,
            state.NavCompactPaneLength,
            searchVisible,
            minSearch,
            maxSearch,
            state.TitleBarPadding,
            new LayoutPadding(0, state.TitleBarInsetsHeight, 0, 0),
            state.TitleBarInsetsHeight,
            canShowSimultaneousAuxiliaryPanels,
            rightPanelVisible,
            rightPanelWidth,
            rightPanelOpenPaneLength,
            effectiveRightPanelMode,
            bottomPanelVisible,
            bottomPanelHeight,
            effectiveBottomPanelMode,
            isOpen && mode == NavigationPaneDisplayMode.Expanded,
            isOpen ? navOpenPaneLength - 6 : state.NavCompactPaneLength - 6,
            canToggleRightPanels,
            canToggleBottomPanel,
            showAuxiliaryTitleBarButtons,
            titleBarInteractiveRegionToken,
            state.SupportsLocalTerminal);
    }

    internal static double ClampNavPaneWidth(double requestedWidth, double currentWidth)
        => ClampResizeWidth(requestedWidth, currentWidth, MinimumNavPaneWidth, MaximumNavPaneWidth);

    internal static double ClampRightPanelWidth(double requestedWidth, double currentWidth)
        => ClampResizeWidth(requestedWidth, currentWidth, MinimumRightPanelWidth, MaximumRightPanelWidth);

    private static double ClampResizeWidth(double requestedWidth, double currentWidth, double minimumWidth, double maximumWidth)
    {
        var candidateWidth = double.IsNaN(requestedWidth) ? currentWidth : requestedWidth;
        return double.IsNaN(candidateWidth)
            ? minimumWidth
            : Math.Clamp(candidateWidth, minimumWidth, maximumWidth);
    }
}
