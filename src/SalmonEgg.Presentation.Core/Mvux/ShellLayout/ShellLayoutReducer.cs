namespace SalmonEgg.Presentation.Core.Mvux.ShellLayout;

public sealed record ShellLayoutReduced(ShellLayoutState State, ShellLayoutSnapshot Snapshot);

public static class ShellLayoutReducer
{
    public static ShellLayoutReduced Reduce(ShellLayoutState state, ShellLayoutAction action)
    {
        var next = action switch
        {
            WindowMetricsChanged m => ApplyWindowMetrics(state, m),
            TitleBarInsetsChanged t => ApplyTitleBarInsets(state, t),
            NavToggleRequested => ApplyNavToggleIntent(state),
            NavPaneOpenIntentRequested r => state with
            {
                UserNavOpenIntent = r.IsOpen,
                IsMinimalPaneOpen = r.IsOpen
            },
            ContentContextChanged c when c.Version < state.ContentContextVersion => state,
            ContentContextChanged { IsChatContext: false } c => state with
            {
                IsChatContext = false,
                ContentContextVersion = c.Version,
                DesiredRightPanelMode = RightPanelMode.None,
                DesiredBottomPanelMode = BottomPanelMode.None,
                LastAuxiliaryPanelArea = AuxiliaryPanelArea.None
            },
            ContentContextChanged { IsChatContext: true } c => state with
            {
                IsChatContext = true,
                ContentContextVersion = c.Version
            },
            ToggleRightPanelRequested when !state.IsChatContext => state,
            ToggleRightPanelRequested t => state with
            {
                DesiredRightPanelMode = state.DesiredRightPanelMode == t.TargetMode ? RightPanelMode.None : t.TargetMode,
                LastAuxiliaryPanelArea = state.DesiredRightPanelMode == t.TargetMode || t.TargetMode == RightPanelMode.None
                    ? state.LastAuxiliaryPanelArea
                    : AuxiliaryPanelArea.Right
            },
            ToggleBottomPanelRequested when !state.IsChatContext => state,
            ToggleBottomPanelRequested when !state.SupportsLocalTerminal => state,
            ToggleBottomPanelRequested => state with
            {
                DesiredBottomPanelMode = state.DesiredBottomPanelMode == BottomPanelMode.None
                    ? BottomPanelMode.Dock
                    : BottomPanelMode.None,
                LastAuxiliaryPanelArea = state.DesiredBottomPanelMode == BottomPanelMode.None
                    ? AuxiliaryPanelArea.Bottom
                    : state.LastAuxiliaryPanelArea
            },
            RightPanelModeChanged when !state.IsChatContext => state,
            RightPanelModeChanged r => state with
            {
                DesiredRightPanelMode = r.Mode,
                LastAuxiliaryPanelArea = r.Mode == RightPanelMode.None ? state.LastAuxiliaryPanelArea : AuxiliaryPanelArea.Right
            },
            BottomPanelModeChanged when !state.IsChatContext => state,
            BottomPanelModeChanged b => state with
            {
                DesiredBottomPanelMode = b.Mode,
                LastAuxiliaryPanelArea = b.Mode == BottomPanelMode.None ? state.LastAuxiliaryPanelArea : AuxiliaryPanelArea.Bottom
            },
            RightPanelResizeRequested r => state with
            {
                RightPanelPreferredWidth = r.AbsoluteWidth,
                LastAuxiliaryPanelArea = state.DesiredRightPanelMode == RightPanelMode.None ? state.LastAuxiliaryPanelArea : AuxiliaryPanelArea.Right
            },
            LeftNavResizeRequested l => state with { NavOpenPaneLength = l.OpenPaneLength },
            _ => state
        };
        var snapshot = ShellLayoutPolicy.Compute(next);
        return new ShellLayoutReduced(next, snapshot);
    }

    private static ShellLayoutState ApplyWindowMetrics(ShellLayoutState state, WindowMetricsChanged metrics)
    {
        var previousMode = ShellLayoutPolicy.Compute(state).NavPaneDisplayMode;
        var next = state with
        {
            WindowMetrics = new WindowMetrics(metrics.Width, metrics.Height, metrics.EffectiveWidth, metrics.EffectiveHeight)
        };
        var nextMode = ShellLayoutPolicy.Compute(next).NavPaneDisplayMode;
        if (previousMode != NavigationPaneDisplayMode.Minimal && nextMode == NavigationPaneDisplayMode.Minimal)
        {
            // Entering minimal should start collapsed until the user explicitly opens the overlay pane.
            next = next with { IsMinimalPaneOpen = false };
        }
        else if (previousMode == NavigationPaneDisplayMode.Minimal && nextMode != NavigationPaneDisplayMode.Minimal)
        {
            // The transient minimal overlay state does not apply outside minimal mode.
            next = next with { IsMinimalPaneOpen = false };
        }

        return next;
    }

    private static ShellLayoutState ApplyTitleBarInsets(ShellLayoutState state, TitleBarInsetsChanged titleBarInsets)
    {
        if (titleBarInsets.Height <= 0)
        {
            return state with
            {
                TitleBarPadding = new LayoutPadding(0, 0, 0, 0),
                TitleBarInsetsHeight = ResolveAppTitleBarHeight(state.TitleBarInsetsHeight)
            };
        }

        return state with
        {
            TitleBarPadding = new LayoutPadding(titleBarInsets.Left, 0, titleBarInsets.Right, 0),
            TitleBarInsetsHeight = titleBarInsets.Height
        };
    }

    private static double ResolveAppTitleBarHeight(double currentHeight)
        => currentHeight > 0 ? currentHeight : ShellLayoutState.DefaultTitleBarHeight;

    private static ShellLayoutState ApplyNavToggleIntent(ShellLayoutState state)
    {
        var snapshot = ShellLayoutPolicy.Compute(state);
        if (snapshot.NavPaneDisplayMode == NavigationPaneDisplayMode.Minimal)
        {
            var nextMinimalOpen = !state.IsMinimalPaneOpen;
            return state with
            {
                IsMinimalPaneOpen = nextMinimalOpen,
                UserNavOpenIntent = nextMinimalOpen
            };
        }

        var nextIntent = !snapshot.IsNavPaneOpen;
        return state with
        {
            UserNavOpenIntent = nextIntent,
            IsMinimalPaneOpen = false
        };
    }
}
