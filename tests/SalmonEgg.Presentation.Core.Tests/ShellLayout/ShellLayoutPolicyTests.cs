using Xunit;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;

namespace SalmonEgg.Presentation.Core.Tests.ShellLayout;

public class ShellLayoutPolicyTests
{
    [Theory]
    [InlineData(1200, NavigationPaneDisplayMode.Expanded, true)]
    [InlineData(800, NavigationPaneDisplayMode.Compact, false)]
    [InlineData(500, NavigationPaneDisplayMode.Minimal, false)]
    public void Policy_MapsWidth_ToPaneMode(double width, NavigationPaneDisplayMode expectedMode, bool expectedOpen)
    {
        var state = ShellLayoutState.Default with { WindowMetrics = new WindowMetrics(width, 700, width, 700) };
        var snapshot = ShellLayoutPolicy.Compute(state);
        Assert.Equal(expectedMode, snapshot.NavPaneDisplayMode);
        Assert.Equal(expectedOpen, snapshot.IsNavPaneOpen);
    }

    [Fact]
    public void Policy_Uses_EffectiveWidth_ForBreakpoints()
    {
        var state = ShellLayoutState.Default with { WindowMetrics = new WindowMetrics(1200, 700, 600, 700) };
        var snapshot = ShellLayoutPolicy.Compute(state);
        Assert.Equal(NavigationPaneDisplayMode.Minimal, snapshot.NavPaneDisplayMode);
    }

    [Fact]
    public void Policy_Uses_TitleBarInsetsHeight()
    {
        var state = ShellLayoutState.Default with { TitleBarInsetsHeight = 60 };
        var snapshot = ShellLayoutPolicy.Compute(state);
        Assert.Equal(60, snapshot.TitleBarHeight);
    }

    [Fact]
    public void Policy_Clamps_And_Hides_RightPanel_WhenTooNarrow()
    {
        var state = ShellLayoutState.Default with
        {
            DesiredRightPanelMode = RightPanelMode.Todo,
            RightPanelPreferredWidth = 400,
            WindowMetrics = new WindowMetrics(1200, 700, 200, 700)
        };

        var snapshot = ShellLayoutPolicy.Compute(state);

        Assert.False(snapshot.RightPanelVisible);
        Assert.Equal(0, snapshot.RightPanelWidth);
    }

    [Fact]
    public void Policy_Disables_RightPanelToggles_WhenRightPanelCannotRender()
    {
        var state = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(1000, 700, 220, 700)
        };

        var snapshot = ShellLayoutPolicy.Compute(state);

        Assert.False(snapshot.CanToggleDiffPanel);
        Assert.False(snapshot.CanToggleTodoPanel);
    }

    [Fact]
    public void Policy_Disables_BottomPanelToggle_WhenBottomPanelCannotRender()
    {
        var state = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(1000, 300, 1000, 300),
            TitleBarInsetsHeight = 60
        };

        var snapshot = ShellLayoutPolicy.Compute(state);

        Assert.False(snapshot.CanToggleBottomPanel);
    }

    [Fact]
    public void Policy_Restores_NavIntent_WhenWide()
    {
        var state = ShellLayoutState.Default with { UserNavOpenIntent = true, WindowMetrics = new WindowMetrics(1200, 700, 1200, 700) };
        var snapshot = ShellLayoutPolicy.Compute(state);
        Assert.True(snapshot.IsNavPaneOpen);
    }

    [Theory]
    [InlineData(800, NavigationPaneDisplayMode.Compact)]
    [InlineData(500, NavigationPaneDisplayMode.Minimal)]
    public void Policy_Allows_UserToOpenPane_InOverlayModes(double width, NavigationPaneDisplayMode expectedMode)
    {
        var state = ShellLayoutState.Default with
        {
            UserNavOpenIntent = true,
            WindowMetrics = new WindowMetrics(width, 700, width, 700)
        };

        var snapshot = ShellLayoutPolicy.Compute(state);

        Assert.Equal(expectedMode, snapshot.NavPaneDisplayMode);
        Assert.True(snapshot.IsNavPaneOpen);
    }

    [Fact]
    public void Policy_SearchBox_Visibility_And_Widths_ByBreakpoint()
    {
        var wide = ShellLayoutPolicy.Compute(ShellLayoutState.Default with { WindowMetrics = new WindowMetrics(1200, 700, 1200, 700) });
        Assert.True(wide.SearchBoxVisible);
        Assert.Equal(220, wide.SearchBoxMinWidth);
        Assert.Equal(360, wide.SearchBoxMaxWidth);

        var medium = ShellLayoutPolicy.Compute(ShellLayoutState.Default with { WindowMetrics = new WindowMetrics(800, 700, 800, 700) });
        Assert.True(medium.SearchBoxVisible);
        Assert.Equal(180, medium.SearchBoxMinWidth);
        Assert.Equal(300, medium.SearchBoxMaxWidth);

        var narrow = ShellLayoutPolicy.Compute(ShellLayoutState.Default with { WindowMetrics = new WindowMetrics(500, 700, 500, 700) });
        Assert.False(narrow.SearchBoxVisible);
    }

    [Fact]
    public void Policy_PreservesDesiredDualOpenButSuppressesOnePanelWhenNarrow()
    {
        var state = ShellLayoutState.Default with
        {
            DesiredRightPanelMode = RightPanelMode.Todo,
            DesiredBottomPanelMode = BottomPanelMode.Dock,
            LastAuxiliaryPanelArea = AuxiliaryPanelArea.Bottom,
            WindowMetrics = new WindowMetrics(1000, 680, 1000, 680)
        };

        var snapshot = ShellLayoutPolicy.Compute(state);

        Assert.False(snapshot.CanShowSimultaneousAuxiliaryPanels);
        Assert.False(snapshot.RightPanelVisible);
        Assert.True(snapshot.BottomPanelVisible);
        Assert.Equal(RightPanelMode.None, snapshot.RightPanelMode);
        Assert.Equal(BottomPanelMode.Dock, snapshot.BottomPanelMode);
    }

    [Fact]
    public void Policy_ShowsBothPanelsWhenWideEnough()
    {
        var state = ShellLayoutState.Default with
        {
            DesiredRightPanelMode = RightPanelMode.Diff,
            DesiredBottomPanelMode = BottomPanelMode.Dock,
            WindowMetrics = new WindowMetrics(1280, 900, 1280, 900)
        };

        var snapshot = ShellLayoutPolicy.Compute(state);

        Assert.True(snapshot.CanShowSimultaneousAuxiliaryPanels);
        Assert.True(snapshot.RightPanelVisible);
        Assert.True(snapshot.BottomPanelVisible);
        Assert.Equal(RightPanelMode.Diff, snapshot.RightPanelMode);
        Assert.Equal(BottomPanelMode.Dock, snapshot.BottomPanelMode);
    }

    [Fact]
    public void Policy_PrefersBottomPanel_WhenLastAreaBottomAndDualUnavailable()
    {
        var state = ShellLayoutState.Default with
        {
            DesiredRightPanelMode = RightPanelMode.Diff,
            DesiredBottomPanelMode = BottomPanelMode.Dock,
            LastAuxiliaryPanelArea = AuxiliaryPanelArea.Bottom,
            WindowMetrics = new WindowMetrics(1000, 680, 1000, 680)
        };

        var snapshot = ShellLayoutPolicy.Compute(state);

        Assert.False(snapshot.CanShowSimultaneousAuxiliaryPanels);
        Assert.True(snapshot.BottomPanelVisible);
        Assert.False(snapshot.RightPanelVisible);
        Assert.Equal(BottomPanelMode.Dock, snapshot.BottomPanelMode);
        Assert.Equal(RightPanelMode.None, snapshot.RightPanelMode);
    }

    [Fact]
    public void Policy_PrefersRightPanel_WhenLastAreaRightAndDualUnavailable()
    {
        var state = ShellLayoutState.Default with
        {
            DesiredRightPanelMode = RightPanelMode.Diff,
            DesiredBottomPanelMode = BottomPanelMode.Dock,
            LastAuxiliaryPanelArea = AuxiliaryPanelArea.Right,
            WindowMetrics = new WindowMetrics(1000, 680, 1000, 680)
        };

        var snapshot = ShellLayoutPolicy.Compute(state);

        Assert.False(snapshot.CanShowSimultaneousAuxiliaryPanels);
        Assert.True(snapshot.RightPanelVisible);
        Assert.False(snapshot.BottomPanelVisible);
        Assert.Equal(RightPanelMode.Diff, snapshot.RightPanelMode);
        Assert.Equal(BottomPanelMode.None, snapshot.BottomPanelMode);
    }

    [Fact]
    public void Policy_FallsBackToBottomPanel_WhenPreferredRightPanelCannotRender()
    {
        var state = ShellLayoutState.Default with
        {
            DesiredRightPanelMode = RightPanelMode.Diff,
            DesiredBottomPanelMode = BottomPanelMode.Dock,
            LastAuxiliaryPanelArea = AuxiliaryPanelArea.Right,
            WindowMetrics = new WindowMetrics(800, 800, 200, 800)
        };

        var snapshot = ShellLayoutPolicy.Compute(state);

        Assert.False(snapshot.CanShowSimultaneousAuxiliaryPanels);
        Assert.False(snapshot.RightPanelVisible);
        Assert.True(snapshot.BottomPanelVisible);
        Assert.Equal(RightPanelMode.None, snapshot.RightPanelMode);
        Assert.Equal(BottomPanelMode.Dock, snapshot.BottomPanelMode);
    }

    [Fact]
    public void Policy_FallsBackToRightPanel_WhenPreferredBottomPanelCannotRender()
    {
        var state = ShellLayoutState.Default with
        {
            DesiredRightPanelMode = RightPanelMode.Diff,
            DesiredBottomPanelMode = BottomPanelMode.Dock,
            LastAuxiliaryPanelArea = AuxiliaryPanelArea.Bottom,
            WindowMetrics = new WindowMetrics(800, 600, 800, 400)
        };

        var snapshot = ShellLayoutPolicy.Compute(state);

        Assert.False(snapshot.CanShowSimultaneousAuxiliaryPanels);
        Assert.True(snapshot.RightPanelVisible);
        Assert.False(snapshot.BottomPanelVisible);
        Assert.Equal(RightPanelMode.Diff, snapshot.RightPanelMode);
        Assert.Equal(BottomPanelMode.None, snapshot.BottomPanelMode);
    }

    [Fact]
    public void Policy_ReactivatesSuppressedRightPanel_WhenBottomMinSizeFailsAfterDualConflict()
    {
        var state = ShellLayoutState.Default with
        {
            DesiredRightPanelMode = RightPanelMode.Diff,
            DesiredBottomPanelMode = BottomPanelMode.Dock,
            LastAuxiliaryPanelArea = AuxiliaryPanelArea.Bottom,
            WindowMetrics = new WindowMetrics(1000, 360, 1000, 360)
        };

        var snapshot = ShellLayoutPolicy.Compute(state);

        Assert.False(snapshot.CanShowSimultaneousAuxiliaryPanels);
        Assert.True(snapshot.RightPanelVisible);
        Assert.False(snapshot.BottomPanelVisible);
        Assert.Equal(RightPanelMode.Diff, snapshot.RightPanelMode);
        Assert.Equal(BottomPanelMode.None, snapshot.BottomPanelMode);
    }
}

public class ShellLayoutReducerBehaviorTests
{
    [Fact]
    public void ToggleBottomPanelSetsDesiredModeAndKeepsBottomWhenDualUnavailable()
    {
        var state = ShellLayoutState.Default with
        {
            DesiredRightPanelMode = RightPanelMode.Diff,
            LastAuxiliaryPanelArea = AuxiliaryPanelArea.Right,
            WindowMetrics = new WindowMetrics(1000, 680, 1000, 680)
        };

        var reduced = ShellLayoutReducer.Reduce(state, new ToggleBottomPanelRequested());

        Assert.Equal(BottomPanelMode.Dock, reduced.State.DesiredBottomPanelMode);
        Assert.Equal(AuxiliaryPanelArea.Bottom, reduced.State.LastAuxiliaryPanelArea);
        Assert.True(reduced.Snapshot.BottomPanelVisible);
        Assert.False(reduced.Snapshot.RightPanelVisible);
        Assert.Equal(BottomPanelMode.Dock, reduced.Snapshot.BottomPanelMode);
        Assert.Equal(RightPanelMode.None, reduced.Snapshot.RightPanelMode);
    }

    [Fact]
    public void ClearAuxiliaryPanelsResetsDesiredModesAndArbitration()
    {
        var state = ShellLayoutState.Default with
        {
            DesiredRightPanelMode = RightPanelMode.Diff,
            DesiredBottomPanelMode = BottomPanelMode.Dock,
            LastAuxiliaryPanelArea = AuxiliaryPanelArea.Bottom
        };

        var reduced = ShellLayoutReducer.Reduce(state, new ClearAuxiliaryPanelsRequested());

        Assert.Equal(RightPanelMode.None, reduced.State.DesiredRightPanelMode);
        Assert.Equal(BottomPanelMode.None, reduced.State.DesiredBottomPanelMode);
        Assert.Equal(AuxiliaryPanelArea.None, reduced.State.LastAuxiliaryPanelArea);
        Assert.False(reduced.Snapshot.RightPanelVisible);
        Assert.False(reduced.Snapshot.BottomPanelVisible);
        Assert.Equal(RightPanelMode.None, reduced.Snapshot.RightPanelMode);
        Assert.Equal(BottomPanelMode.None, reduced.Snapshot.BottomPanelMode);
    }
}
