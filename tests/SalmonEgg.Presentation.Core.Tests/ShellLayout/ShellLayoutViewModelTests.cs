using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Uno.Extensions.Reactive;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using CommunityToolkit.Mvvm.ComponentModel;

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
    }

    [Fact]
    public async Task ViewModel_ProjectsSnapshot()
    {
        await using var store = new FakeShellLayoutStore();
        using var vm = new ShellLayoutViewModel(store);

        await store.SnapshotState.Update(_ => new ShellLayoutSnapshot(
            NavigationPaneDisplayMode.Compact, false, 300, 72,
            false, 0, 0, new LayoutPadding(4, 0, 4, 0), new LayoutPadding(0, 0, 0, 0), 60,
            false, 0, RightPanelMode.None, false, 0), default);

        await Task.Delay(100);

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
            false, 0, RightPanelMode.None, false, 0), default);

        await Task.Delay(50);
        Assert.True(vm.IsNavPaneOpen);

        syncContext.RunAll();

        Assert.False(vm.IsNavPaneOpen);
        Assert.Equal(NavigationPaneDisplayMode.Compact, vm.NavPaneDisplayMode);
    }

    private sealed class FakeShellLayoutStore : IShellLayoutStore, IAsyncDisposable
    {
        private readonly IState<ShellLayoutState> _state;

        public FakeShellLayoutStore()
        {
            SnapshotState = State.Value(new object(), () => ShellLayoutPolicy.Compute(ShellLayoutState.Default));
            _state = State.Value(new object(), () => ShellLayoutState.Default);
        }

        public IFeed<ShellLayoutSnapshot> Snapshot => SnapshotState;
        public IState<ShellLayoutSnapshot> SnapshotState { get; }
        public ValueTask Dispatch(ShellLayoutAction action) => ValueTask.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            await SnapshotState.DisposeAsync();
            await _state.DisposeAsync();
        }
    }

    private sealed class PumpingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Enqueue((d, state));
        }

        public void RunAll()
        {
            while (_queue.Count > 0)
            {
                var work = _queue.Dequeue();
                work.Callback(work.State);
            }
        }
    }
}
