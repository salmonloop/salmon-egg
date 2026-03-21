using System;
using System.Threading;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Services;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public class RightPanelServiceTests
{
    [Fact]
    public async Task Dispose_ShouldCompleteAfterModeChange()
    {
        using var store = new TestShellLayoutStore();
        var service = new RightPanelService(store);
        using var signal = new ManualResetEventSlim(false);
        service.ModeChanged += (_, _) => signal.Set();

        await store.Dispatch(new RightPanelModeChanged(RightPanelMode.Todo));
        Assert.True(signal.Wait(TimeSpan.FromSeconds(1)));

        var disposeTask = Task.Run(service.Dispose);
        var completedTask = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(disposeTask, completedTask);
        await disposeTask;
    }

    [Fact]
    public async Task CurrentMode_ShouldChangeStateAndNotify()
    {
        using var store = new TestShellLayoutStore();
        using var service = new RightPanelService(store);
        using var signal = new ManualResetEventSlim(false);
        var modeChangedCalled = 0;
        service.ModeChanged += (_, _) => modeChangedCalled++;
        service.ModeChanged += (_, _) => signal.Set();

        await store.Dispatch(new RightPanelModeChanged(RightPanelMode.Todo));
        Assert.True(signal.Wait(TimeSpan.FromSeconds(1)));

        Assert.Equal(RightPanelMode.Todo, service.CurrentMode);
        Assert.Equal(1, modeChangedCalled);
    }

    [Fact]
    public async Task CurrentMode_ShouldNotNotifyIfValueIsSame()
    {
        using var store = new TestShellLayoutStore();
        using var service = new RightPanelService(store);
        using var signal = new ManualResetEventSlim(false);
        service.ModeChanged += (_, _) => signal.Set();
        await store.Dispatch(new RightPanelModeChanged(RightPanelMode.Diff));
        Assert.True(signal.Wait(TimeSpan.FromSeconds(1)));
        var modeChangedCalled = 0;
        service.ModeChanged += (_, _) => modeChangedCalled++;

        await store.Dispatch(new RightPanelModeChanged(RightPanelMode.Diff));

        Assert.Equal(RightPanelMode.Diff, service.CurrentMode);
        Assert.Equal(0, modeChangedCalled);
    }

    private sealed class TestShellLayoutStore : IShellLayoutStore, IDisposable
    {
        private readonly IState<ShellLayoutState> _state;
        private readonly IState<ShellLayoutSnapshot> _snapshot;

        public TestShellLayoutStore()
        {
            _state = Uno.Extensions.Reactive.State.Value(new object(), () => ShellLayoutState.Default);
            _snapshot = Uno.Extensions.Reactive.State.Value(new object(), () => ShellLayoutPolicy.Compute(ShellLayoutState.Default));
        }

        public IFeed<ShellLayoutState> State => _state;
        public IFeed<ShellLayoutSnapshot> Snapshot => _snapshot;

        public async ValueTask Dispatch(ShellLayoutAction action)
        {
            ShellLayoutReduced? reduced = null;

            await _state.Update(s =>
            {
                reduced = ShellLayoutReducer.Reduce(s!, action);
                return reduced.State;
            }, default);

            if (reduced is null)
            {
                return;
            }

            await _snapshot.Update(_ => reduced.Snapshot, default);
        }

        public void Dispose()
        {
            if (_snapshot is IAsyncDisposable snapshotDisposable)
            {
                snapshotDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            if (_state is IAsyncDisposable stateDisposable)
            {
                stateDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }
}
