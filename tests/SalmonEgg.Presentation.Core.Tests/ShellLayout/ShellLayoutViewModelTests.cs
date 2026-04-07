using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uno.Extensions.Reactive;
using Xunit;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;

namespace SalmonEgg.Presentation.Core.Tests.ShellLayout;

public class ShellLayoutViewModelTests
{
    [Fact]
    public async Task ViewModel_SeedsDefaultSnapshotImmediately()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store);

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
        using var vm = new ShellLayoutViewModel(store);

        Assert.Equal(NavigationPaneDisplayMode.Compact, vm.NavPaneDisplayMode);
        Assert.False(vm.IsNavPaneOpen);
    }

    [Fact]
    public async Task ViewModel_ProjectsSnapshot()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store);

        await store.SnapshotState.Update(_ => new ShellLayoutSnapshot(
            NavigationPaneDisplayMode.Compact, false, 300, 72,
            false, 0, 0, new LayoutPadding(4, 0, 4, 0), new LayoutPadding(0, 0, 0, 0), 60,
            false, false, 0, RightPanelMode.None, false, 0, BottomPanelMode.None, false, 0), default);

        await WaitForConditionAsync(
            () => vm.NavPaneDisplayMode == NavigationPaneDisplayMode.Compact
                && !vm.IsNavPaneOpen
                && vm.TitleBarHeight == 60);

        Assert.Equal(NavigationPaneDisplayMode.Compact, vm.NavPaneDisplayMode);
        Assert.False(vm.IsNavPaneOpen);
        Assert.Equal(60, vm.TitleBarHeight);
    }

    [Fact]
    public async Task ViewModel_ProjectsSnapshot_OnProvidedSynchronizationContext()
    {
        await using var store = new FakeShellLayoutStore();
        var syncContext = new PumpingSynchronizationContext();
        using var vm = new ShellLayoutViewModel(store, syncContext);

        await store.SnapshotState.Update(_ => new ShellLayoutSnapshot(
            NavigationPaneDisplayMode.Compact, false, 300, 72,
            false, 0, 0, new LayoutPadding(4, 0, 4, 0), new LayoutPadding(0, 0, 0, 0), 60,
            false, false, 0, RightPanelMode.None, false, 0, BottomPanelMode.None, false, 0), default);

        await Task.Delay(50);
        Assert.True(vm.IsNavPaneOpen);

        syncContext.RunAll();

        Assert.False(vm.IsNavPaneOpen);
        Assert.Equal(NavigationPaneDisplayMode.Compact, vm.NavPaneDisplayMode);
    }

    [Fact]
    public async Task ViewModel_ProjectsBottomPanelSnapshot()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store);

        await store.SnapshotState.Update(_ => new ShellLayoutSnapshot(
            NavigationPaneDisplayMode.Expanded, true, 300, 72,
            true, 220, 360, new LayoutPadding(0, 0, 0, 0), new LayoutPadding(0, 0, 0, 0), 48,
            true, false, 0, RightPanelMode.None, true, 240, BottomPanelMode.Dock, true, 294), default);

        await WaitForConditionAsync(
            () => vm.BottomPanelVisible
                && vm.BottomPanelHeight == 240
                && vm.BottomPanelMode == BottomPanelMode.Dock
                && vm.CanShowSimultaneousAuxiliaryPanels);

        Assert.True(vm.BottomPanelVisible);
        Assert.Equal(240, vm.BottomPanelHeight);
        Assert.Equal(BottomPanelMode.Dock, vm.BottomPanelMode);
        Assert.True(vm.CanShowSimultaneousAuxiliaryPanels);
    }

    [Fact]
    public async Task ViewModel_ProjectsDesiredModes_EvenWhenEffectiveIsSuppressed()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store);

        // Simulate a narrow shell where right+bottom cannot be shown simultaneously.
        // User intent keeps both desired modes non-None, but policy suppresses the bottom panel.
        var desired = ShellLayoutState.Default with
        {
            WindowMetrics = new WindowMetrics(1000, 650, 1000, 650),
            DesiredRightPanelMode = RightPanelMode.Diff,
            DesiredBottomPanelMode = BottomPanelMode.Dock,
            LastAuxiliaryPanelArea = AuxiliaryPanelArea.Right
        };

        await store.StateState.Update(_ => desired, default);
        await store.SnapshotState.Update(_ => ShellLayoutPolicy.Compute(desired), default);

        await WaitForConditionAsync(
            () => vm.DesiredRightPanelMode == RightPanelMode.Diff
                && vm.DesiredBottomPanelMode == BottomPanelMode.Dock
                && vm.RightPanelMode == RightPanelMode.Diff
                && vm.BottomPanelMode == BottomPanelMode.None
                && !vm.BottomPanelVisible);

        Assert.Equal(RightPanelMode.Diff, vm.DesiredRightPanelMode);
        Assert.Equal(BottomPanelMode.Dock, vm.DesiredBottomPanelMode);
        Assert.Equal(RightPanelMode.Diff, vm.RightPanelMode);
        Assert.Equal(BottomPanelMode.None, vm.BottomPanelMode);
        Assert.False(vm.BottomPanelVisible);
    }

    [Fact]
    public async Task ViewModel_ToggleRightPanelCommands_DispatchExpectedIntent()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store);

        await vm.ToggleDiffPanelCommand.ExecuteAsync(null);
        await vm.ToggleTodoPanelCommand.ExecuteAsync(null);

        Assert.Contains(store.DispatchedActions, action =>
            action is ToggleRightPanelRequested { TargetMode: RightPanelMode.Diff });
        Assert.Contains(store.DispatchedActions, action =>
            action is ToggleRightPanelRequested { TargetMode: RightPanelMode.Todo });
    }

    [Fact]
    public async Task ViewModel_ToggleBottomPanelCommand_DispatchExpectedIntent()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store);

        await vm.ToggleBottomPanelCommand.ExecuteAsync(null);

        Assert.Contains(store.DispatchedActions, action => action is ToggleBottomPanelRequested);
    }

    [Fact]
    public async Task ViewModel_ProjectsPanelToggleAvailability_FromSnapshot()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store);

        await store.SnapshotState.Update(_ => new ShellLayoutSnapshot(
            NavigationPaneDisplayMode.Compact, false, 300, 72,
            false, 0, 0, new LayoutPadding(4, 0, 4, 0), new LayoutPadding(0, 0, 0, 0), 60,
            false, false, 0, RightPanelMode.None, false, 0, BottomPanelMode.None, false, 0,
            false, false, false), default);

        await WaitForConditionAsync(
            () => !vm.CanToggleDiffPanel
                  && !vm.CanToggleTodoPanel
                  && !vm.CanToggleBottomPanel);

        Assert.False(vm.CanToggleDiffPanel);
        Assert.False(vm.CanToggleTodoPanel);
        Assert.False(vm.CanToggleBottomPanel);
    }

    [Fact]
    public async Task ViewModel_DoesNotDispatchToggleCommands_WhenDisabled()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store);

        await store.SnapshotState.Update(_ => new ShellLayoutSnapshot(
            NavigationPaneDisplayMode.Compact, false, 300, 72,
            false, 0, 0, new LayoutPadding(4, 0, 4, 0), new LayoutPadding(0, 0, 0, 0), 60,
            false, false, 0, RightPanelMode.None, false, 0, BottomPanelMode.None, false, 0,
            false, false, false), default);

        await WaitForConditionAsync(
            () => !vm.CanToggleDiffPanel
                  && !vm.CanToggleTodoPanel
                  && !vm.CanToggleBottomPanel);

        await vm.ToggleDiffPanelCommand.ExecuteAsync(null);
        await vm.ToggleTodoPanelCommand.ExecuteAsync(null);
        await vm.ToggleBottomPanelCommand.ExecuteAsync(null);

        Assert.Empty(store.DispatchedActions);
    }

    private static async Task WaitForConditionAsync(
        System.Func<bool> predicate,
        int maxAttempts = 20,
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

    private sealed class PumpingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback? Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (d is null)
            {
                return;
            }

            _queue.Enqueue((d, state));
        }

        public void RunAll()
        {
            while (_queue.Count > 0)
            {
                var work = _queue.Dequeue();
                work.Callback?.Invoke(work.State);
            }
        }
    }
}
