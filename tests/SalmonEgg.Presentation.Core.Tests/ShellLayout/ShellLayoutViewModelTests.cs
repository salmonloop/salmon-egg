using System.Collections.Generic;
using System.Threading.Tasks;
using Uno.Extensions.Reactive;
using Xunit;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;

namespace SalmonEgg.Presentation.Core.Tests.ShellLayout;

public class ShellLayoutViewModelTests
{
    [Fact]
    public async Task ViewModel_SeedsDefaultSnapshotImmediately()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store, new ImmediateUiDispatcher());

        Assert.Equal(NavigationPaneDisplayMode.Expanded, vm.NavPaneDisplayMode);
        Assert.True(vm.IsNavPaneOpen);
        Assert.Equal(300, vm.NavOpenPaneLength);
        Assert.Equal(72, vm.NavCompactPaneLength);
        Assert.True(vm.SearchBoxVisible);
        Assert.Equal(48, vm.TitleBarHeight);
        Assert.False(vm.BottomPanelVisible);
    }

    [Fact]
    public async Task ViewModel_SeedsCurrentStoreSnapshotImmediately()
    {
        var initialState = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(800, 720, 800, 720),
            UserNavOpenIntent = false
        };
        await using var store = new FakeShellLayoutStore(initialState);
        using var vm = new ShellLayoutViewModel(store, new ImmediateUiDispatcher());

        Assert.Equal(NavigationPaneDisplayMode.Compact, vm.NavPaneDisplayMode);
        Assert.False(vm.IsNavPaneOpen);
    }

    [Fact]
    public async Task ViewModel_ProjectsSnapshot()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store, new ImmediateUiDispatcher());

        await store.SnapshotState.Update(_ => new ShellLayoutSnapshot(
            NavigationPaneDisplayMode.Compact, false, 300, 72,
            false, 0, 0, new LayoutPadding(4, 0, 4, 0), new LayoutPadding(0, 0, 0, 0), 60,
            false, false, 0, 0, RightPanelMode.None, false, 0, BottomPanelMode.None, false, 0), default);

        await WaitForConditionAsync(
            () => vm.NavPaneDisplayMode == NavigationPaneDisplayMode.Compact
                && !vm.IsNavPaneOpen
                && vm.TitleBarHeight == 60);

        Assert.Equal(NavigationPaneDisplayMode.Compact, vm.NavPaneDisplayMode);
        Assert.False(vm.IsNavPaneOpen);
        Assert.Equal(60, vm.TitleBarHeight);
    }

    [Fact]
    public async Task ViewModel_ProjectsSnapshot_OnProvidedDispatcher()
    {
        var initialState = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(900, 720, 900, 720),
            UserNavOpenIntent = false,
            TitleBarInsetsHeight = 60
        };
        await using var store = new FakeShellLayoutStore(initialState);
        var dispatcher = new QueueingUiDispatcher();
        using var vm = new ShellLayoutViewModel(store, dispatcher);

        Assert.False(vm.IsNavPaneOpen);
        Assert.Equal(NavigationPaneDisplayMode.Compact, vm.NavPaneDisplayMode);
        Assert.Equal(60, vm.TitleBarHeight);
    }

    [Fact]
    public async Task ViewModel_ProjectsBottomPanelSnapshot()
    {
        var initialState = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(1280, 900, 1280, 900),
            IsChatContext = true,
            DesiredBottomPanelMode = BottomPanelMode.Dock,
            BottomPanelPreferredHeight = 240,
            LastAuxiliaryPanelArea = AuxiliaryPanelArea.Bottom
        };
        await using var store = new FakeShellLayoutStore(initialState);
        using var vm = new ShellLayoutViewModel(store, new ImmediateUiDispatcher());

        Assert.True(vm.BottomPanelVisible);
        Assert.Equal(240, vm.BottomPanelHeight);
        Assert.Equal(BottomPanelMode.Dock, vm.BottomPanelMode);
        Assert.True(vm.CanShowSimultaneousAuxiliaryPanels);
    }

    [Fact]
    public async Task ViewModel_ProjectsRightPanelOpenPaneLengthBeforeOpeningPane()
    {
        var initialState = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(500, 900, 500, 900),
            IsChatContext = true,
            DesiredRightPanelMode = RightPanelMode.TaskOverview,
            RightPanelPreferredWidth = 400
        };
        await using var store = new FakeShellLayoutStore(initialState);
        using var vm = new ShellLayoutViewModel(store, new ImmediateUiDispatcher());
        Assert.False(vm.RightPanelVisible);
        Assert.Equal(0, vm.RightPanelWidth);
        Assert.Equal(0, vm.RightPanelOpenPaneLength);

        var changes = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changes.Add(args.PropertyName);
            }
        };

        await store.Dispatch(new WindowMetricsChanged(1280, 900, 1280, 900));
        await WaitForConditionAsync(() => vm.RightPanelVisible);

        Assert.Equal(400, vm.RightPanelOpenPaneLength);
        Assert.Equal(400, vm.RightPanelWidth);
        Assert.True(
            changes.IndexOf(nameof(vm.RightPanelOpenPaneLength)) >= 0
            && changes.IndexOf(nameof(vm.RightPanelVisible)) >= 0
            && changes.IndexOf(nameof(vm.RightPanelOpenPaneLength)) < changes.IndexOf(nameof(vm.RightPanelVisible)),
            "SplitView.OpenPaneLength must update before SplitView.IsPaneOpen changes.");
    }

    [Fact]
    public async Task ViewModel_ProjectsDesiredModes_EvenWhenEffectiveIsSuppressed()
    {
        // Simulate a narrow shell where right+bottom cannot be shown simultaneously.
        // User intent keeps both desired modes non-None, but policy suppresses the bottom panel.
        var desired = ShellLayoutState.Default with
        {
            IsChatContext = true,
            WindowMetrics = new WindowMetrics(1000, 650, 1000, 650),
            DesiredRightPanelMode = RightPanelMode.TaskOverview,
            DesiredBottomPanelMode = BottomPanelMode.Dock,
            LastAuxiliaryPanelArea = AuxiliaryPanelArea.Right
        };
        await using var store = new FakeShellLayoutStore(desired);
        using var vm = new ShellLayoutViewModel(store, new ImmediateUiDispatcher());

        Assert.Equal(RightPanelMode.TaskOverview, vm.DesiredRightPanelMode);
        Assert.Equal(BottomPanelMode.Dock, vm.DesiredBottomPanelMode);
        Assert.Equal(RightPanelMode.TaskOverview, vm.RightPanelMode);
        Assert.Equal(BottomPanelMode.None, vm.BottomPanelMode);
        Assert.False(vm.BottomPanelVisible);
    }

    [Fact]
    public async Task ViewModel_ToggleTaskOverviewPanelCommand_DispatchExpectedIntent()
    {
        await using var store = new FakeShellLayoutStore(ShellLayoutState.Default with { IsChatContext = true });
        using var vm = new ShellLayoutViewModel(store, new ImmediateUiDispatcher());

        await vm.ToggleTaskOverviewPanelCommand.ExecuteAsync(null);

        Assert.Contains(store.DispatchedActions, action =>
            action is ToggleRightPanelRequested { TargetMode: RightPanelMode.TaskOverview });
    }

    [Fact]
    public async Task ViewModel_ToggleBottomPanelCommand_DispatchExpectedIntent()
    {
        await using var store = new FakeShellLayoutStore(ShellLayoutState.Default with { IsChatContext = true });
        using var vm = new ShellLayoutViewModel(store, new ImmediateUiDispatcher());

        await vm.ToggleBottomPanelCommand.ExecuteAsync(null);

        Assert.Contains(store.DispatchedActions, action => action is ToggleBottomPanelRequested);
    }

    [Fact]
    public async Task ViewModel_ProjectsPanelToggleAvailability_FromSnapshot()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store, new ImmediateUiDispatcher());

        await store.SnapshotState.Update(_ => new ShellLayoutSnapshot(
            NavigationPaneDisplayMode.Compact, false, 300, 72,
            false, 0, 0, new LayoutPadding(4, 0, 4, 0), new LayoutPadding(0, 0, 0, 0), 60,
            false, false, 0, 0, RightPanelMode.None, false, 0, BottomPanelMode.None, false, 0,
            false, false, false), default);

        await WaitForConditionAsync(
            () => !vm.CanToggleTaskOverviewPanel
                  && !vm.CanToggleBottomPanel);

        Assert.False(vm.CanToggleTaskOverviewPanel);
        Assert.False(vm.CanToggleBottomPanel);
    }

    [Fact]
    public async Task ViewModel_ProjectsAuxiliaryTitleBarButtonsVisibility_FromSnapshot()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store, new ImmediateUiDispatcher());

        await store.SnapshotState.Update(_ => new ShellLayoutSnapshot(
            NavigationPaneDisplayMode.Compact, false, 300, 72,
            false, 0, 0, new LayoutPadding(4, 0, 4, 0), new LayoutPadding(0, 0, 0, 0), 60,
            false, false, 0, 0, RightPanelMode.None, false, 0, BottomPanelMode.None, false, 0,
            false, false,
            true,
            42), default);

        await WaitForConditionAsync(() => vm.ShowAuxiliaryTitleBarButtons);

        Assert.True(vm.ShowAuxiliaryTitleBarButtons);
        Assert.Equal(42, vm.TitleBarInteractiveRegionToken);
    }

    [Fact]
    public async Task ViewModel_DoesNotDispatchToggleCommands_WhenDisabled()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store, new ImmediateUiDispatcher());

        await store.SnapshotState.Update(_ => new ShellLayoutSnapshot(
            NavigationPaneDisplayMode.Compact, false, 300, 72,
            false, 0, 0, new LayoutPadding(4, 0, 4, 0), new LayoutPadding(0, 0, 0, 0), 60,
            false, false, 0, 0, RightPanelMode.None, false, 0, BottomPanelMode.None, false, 0,
            false, false, false), default);

        await WaitForConditionAsync(
            () => !vm.CanToggleTaskOverviewPanel
                  && !vm.CanToggleBottomPanel);

        await vm.ToggleTaskOverviewPanelCommand.ExecuteAsync(null);
        await vm.ToggleBottomPanelCommand.ExecuteAsync(null);

        Assert.Empty(store.DispatchedActions);
    }

    private static async Task WaitForConditionAsync(
        System.Func<bool> predicate,
        int maxAttempts = 3000,
        int delayMs = 10)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(delayMs);
        }
    }

    private sealed class FakeShellLayoutStore : IShellLayoutStore, IAsyncDisposable
    {
        private readonly IState<ShellLayoutState> _state;
        public List<ShellLayoutAction> DispatchedActions { get; } = new();

        public FakeShellLayoutStore(ShellLayoutState? initialState = null)
        {
            CurrentState = initialState ?? ShellLayoutState.Default;
            CurrentSnapshot = ShellLayoutPolicy.Compute(CurrentState);
            SnapshotState = Uno.Extensions.Reactive.State.Value(new object(), () => CurrentSnapshot);
            _state = Uno.Extensions.Reactive.State.Value(new object(), () => CurrentState);
        }

        public IFeed<ShellLayoutState> State => _state;
        public IFeed<ShellLayoutSnapshot> Snapshot => SnapshotState;
        public IState<ShellLayoutSnapshot> SnapshotState { get; }
        public IState<ShellLayoutState> StateState => _state;
        public ShellLayoutState CurrentState { get; private set; }
        public ShellLayoutSnapshot CurrentSnapshot { get; private set; }
        public async ValueTask Dispatch(ShellLayoutAction action)
        {
            DispatchedActions.Add(action);
            var reduced = ShellLayoutReducer.Reduce(CurrentState, action);
            CurrentState = reduced.State;
            CurrentSnapshot = reduced.Snapshot;
            await _state.Update(_ => CurrentState, default);
            await SnapshotState.Update(_ => CurrentSnapshot, default);
        }

        public async ValueTask DisposeAsync()
        {
            await SnapshotState.DisposeAsync();
            await _state.DisposeAsync();
        }
    }
}
