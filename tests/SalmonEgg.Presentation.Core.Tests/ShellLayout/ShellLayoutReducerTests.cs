using Xunit;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;

namespace SalmonEgg.Presentation.Core.Tests.ShellLayout;

public class ShellLayoutReducerTests
{
    [Fact]
    public void Reducer_UpdatesSnapshot_WhenWindowMetricsChange()
    {
        var state = ShellLayoutState.Default;
        var reduced = ShellLayoutReducer.Reduce(state, new WindowMetricsChanged(800, 700, 800, 700));
        Assert.Equal(NavigationPaneDisplayMode.Compact, reduced.Snapshot.NavPaneDisplayMode);
    }

    [Fact]
    public void Reducer_Tracks_TitleBarHeight()
    {
        var state = ShellLayoutState.Default;
        var reduced = ShellLayoutReducer.Reduce(state, new TitleBarInsetsChanged(10, 10, 60));
        Assert.Equal(60, reduced.Snapshot.TitleBarHeight);
    }

    [Fact]
    public void Reducer_Preserves_NavIntent_Across_Resize()
    {
        var state = ShellLayoutState.Default with { UserNavOpenIntent = true };
        var reduced = ShellLayoutReducer.Reduce(state, new WindowMetricsChanged(1200, 700, 1200, 700));
        Assert.True(reduced.Snapshot.IsNavPaneOpen);
    }

    [Fact]
    public void Reducer_Stores_Intent_InNarrow_ThenRestores_OnWide()
    {
        var state = ShellLayoutState.Default;
        var narrow = ShellLayoutReducer.Reduce(state, new WindowMetricsChanged(500, 700, 500, 700)).State;
        var toggled = ShellLayoutReducer.Reduce(narrow, new NavToggleRequested("TitleBar")).State;
        var wide = ShellLayoutReducer.Reduce(toggled, new WindowMetricsChanged(1200, 700, 1200, 700));
        Assert.True(wide.Snapshot.IsNavPaneOpen);
    }

    [Fact]
    public void Reducer_EnteringMinimal_CollapsesPane_AndPreservesOpenIntent()
    {
        var state = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(900, 700, 900, 700),
            UserNavOpenIntent = true
        };

        var reduced = ShellLayoutReducer.Reduce(state, new WindowMetricsChanged(500, 700, 500, 700));

        Assert.Equal(NavigationPaneDisplayMode.Minimal, reduced.Snapshot.NavPaneDisplayMode);
        Assert.False(reduced.Snapshot.IsNavPaneOpen);
        Assert.True(reduced.State.UserNavOpenIntent);
    }

    [Fact]
    public void Reducer_StayingInMinimal_DoesNotOverrideFreshUserOpenIntent()
    {
        var state = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(500, 700, 500, 700),
            UserNavOpenIntent = false
        };

        var toggledOpen = ShellLayoutReducer.Reduce(state, new NavToggleRequested("TitleBar"));
        var resizedWithinMinimal = ShellLayoutReducer.Reduce(
            toggledOpen.State,
            new WindowMetricsChanged(520, 700, 520, 700));

        Assert.Equal(NavigationPaneDisplayMode.Minimal, resizedWithinMinimal.Snapshot.NavPaneDisplayMode);
        Assert.True(resizedWithinMinimal.Snapshot.IsNavPaneOpen);
        Assert.True(resizedWithinMinimal.State.UserNavOpenIntent);
    }

    [Fact]
    public void Reducer_ExitMinimal_RestoresExpandedPaneWhenUserIntentWasOpen()
    {
        var expanded = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(1200, 700, 1200, 700),
            UserNavOpenIntent = true
        };

        var minimal = ShellLayoutReducer.Reduce(
            expanded,
            new WindowMetricsChanged(500, 700, 500, 700));
        var backToExpanded = ShellLayoutReducer.Reduce(
            minimal.State,
            new WindowMetricsChanged(1200, 700, 1200, 700));

        Assert.Equal(NavigationPaneDisplayMode.Minimal, minimal.Snapshot.NavPaneDisplayMode);
        Assert.False(minimal.Snapshot.IsNavPaneOpen);
        Assert.Equal(NavigationPaneDisplayMode.Expanded, backToExpanded.Snapshot.NavPaneDisplayMode);
        Assert.True(backToExpanded.Snapshot.IsNavPaneOpen);
    }

    [Fact]
    public void Reducer_Toggle_Uses_CurrentOpenState()
    {
        var state = ShellLayoutState.Default with { WindowMetrics = new WindowMetrics(1200, 700, 1200, 700) };
        var reduced = ShellLayoutReducer.Reduce(state, new NavToggleRequested("TitleBar"));
        Assert.False(reduced.State.UserNavOpenIntent);
    }

    [Fact]
    public void Reducer_Toggle_OpensPane_InCompactMode()
    {
        var state = ShellLayoutState.Default with { WindowMetrics = new WindowMetrics(800, 700, 800, 700) };

        var reduced = ShellLayoutReducer.Reduce(state, new NavToggleRequested("TitleBar"));

        Assert.True(reduced.Snapshot.IsNavPaneOpen);
    }

    [Fact]
    public void Reducer_ToggleTwiceInMinimal_ShouldRestoreClosedIntent()
    {
        var state = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(500, 700, 500, 700),
            UserNavOpenIntent = false
        };

        var firstToggle = ShellLayoutReducer.Reduce(state, new NavToggleRequested("TitleBar"));
        var secondToggle = ShellLayoutReducer.Reduce(firstToggle.State, new NavToggleRequested("TitleBar"));

        Assert.True(firstToggle.State.UserNavOpenIntent);
        Assert.False(secondToggle.State.UserNavOpenIntent);
    }

    [Fact]
    public void Reducer_ToggleInMinimal_ShouldOpenPane()
    {
        var state = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(500, 700, 500, 700),
            UserNavOpenIntent = true
        };

        var minimal = ShellLayoutReducer.Reduce(state, new WindowMetricsChanged(500, 700, 500, 700));
        Assert.Equal(NavigationPaneDisplayMode.Minimal, minimal.Snapshot.NavPaneDisplayMode);
        Assert.False(minimal.Snapshot.IsNavPaneOpen);

        var toggled = ShellLayoutReducer.Reduce(minimal.State, new NavToggleRequested("TitleBar"));

        Assert.True(toggled.Snapshot.IsNavPaneOpen);
    }

    [Fact]
    public void Reducer_SetNavPaneOpenIntent_OverridesCurrentIntent()
    {
        var state = ShellLayoutState.Default with { UserNavOpenIntent = true };

        var reduced = ShellLayoutReducer.Reduce(state, new NavPaneOpenIntentRequested(false, "PaneClosing"));

        Assert.False(reduced.State.UserNavOpenIntent);
    }

    [Fact]
    public void Reducer_ContentContextChange_ProjectsAuxiliaryTitleBarVisibility()
    {
        var state = ShellLayoutState.Default with { IsChatContext = false };

        var reduced = ShellLayoutReducer.Reduce(state, new ContentContextChanged(IsChatContext: true));

        Assert.True(reduced.State.IsChatContext);
        Assert.True(reduced.Snapshot.ShowAuxiliaryTitleBarButtons);
    }

    [Fact]
    public void Reducer_ContentContextChange_ChangesTitleBarInteractiveRegionToken()
    {
        var state = ShellLayoutState.Default with { IsChatContext = false };
        var before = ShellLayoutPolicy.Compute(state);

        var reduced = ShellLayoutReducer.Reduce(state, new ContentContextChanged(IsChatContext: true));

        Assert.NotEqual(before.TitleBarInteractiveRegionToken, reduced.Snapshot.TitleBarInteractiveRegionToken);
    }
}
