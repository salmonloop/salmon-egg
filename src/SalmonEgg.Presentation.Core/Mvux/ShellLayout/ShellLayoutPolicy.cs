using System;

namespace SalmonEgg.Presentation.Core.Mvux.ShellLayout;

public static class ShellLayoutPolicy
{
    private const double RightPanelMinWidth = 240;
    private const double RightPanelMaxWidth = 520;
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
            _ => state.UserNavOpenIntent == true
        };

        var searchVisible = mode != NavigationPaneDisplayMode.Minimal;
        var minSearch = mode == NavigationPaneDisplayMode.Expanded ? 220 : 180;
        var maxSearch = mode == NavigationPaneDisplayMode.Expanded ? 360 : 300;

        var maxRightPanelWidth = Math.Min(RightPanelMaxWidth, availableWidth);
        var contentHeight = Math.Max(0, availableHeight - state.TitleBarInsetsHeight);
        var maxBottomPanelHeight = Math.Min(BottomPanelMaxHeight, Math.Max(0, contentHeight - MinimumChatRegionHeight));

        var canShowSimultaneousAuxiliaryPanels =
            availableWidth >= MinimumDualPanelWidth && availableHeight >= MinimumDualPanelHeight;

        var rightPanelEligible = state.DesiredRightPanelMode != RightPanelMode.None
            && maxRightPanelWidth >= RightPanelMinWidth;
        var bottomPanelEligible = state.DesiredBottomPanelMode != BottomPanelMode.None
            && maxBottomPanelHeight >= BottomPanelMinHeight;

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

        var rightPanelVisible = effectiveRightPanelMode != RightPanelMode.None;
        double rightPanelWidth = 0;
        if (rightPanelVisible)
        {
            rightPanelWidth = Math.Clamp(state.RightPanelPreferredWidth, RightPanelMinWidth, maxRightPanelWidth);
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
            state.NavOpenPaneLength,
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
            effectiveRightPanelMode,
            bottomPanelVisible,
            bottomPanelHeight,
            effectiveBottomPanelMode,
            isOpen && mode == NavigationPaneDisplayMode.Expanded,
            isOpen ? state.NavOpenPaneLength - 6 : state.NavCompactPaneLength - 6);
    }
}
