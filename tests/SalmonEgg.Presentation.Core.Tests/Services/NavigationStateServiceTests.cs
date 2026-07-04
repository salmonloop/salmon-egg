using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public class NavigationStateServiceTests
{
    [Fact]
    public async Task IsPaneOpen_ShouldChangeStateAndNotify()
    {
        await using var store = new TestShellLayoutStore();
        using var service = new NavigationStateService(store, new ImmediateUiDispatcher());
        using var signal = new ManualResetEventSlim(false);
        var changedCount = 0;
        service.PaneStateChanged += (_, _) => changedCount++;
        service.PaneStateChanged += (_, _) => signal.Set();
        var initialIsPaneOpen = service.IsPaneOpen;

        await store.Dispatch(new NavToggleRequested("test"));
        Assert.True(signal.Wait(TimeSpan.FromSeconds(1)));

        Assert.Equal(!initialIsPaneOpen, service.IsPaneOpen);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public async Task IsPaneOpen_ShouldNotNotifyIfValueIsSame()
    {
        await using var store = new TestShellLayoutStore();
        using var service = new NavigationStateService(store, new ImmediateUiDispatcher());
        using var signal = new ManualResetEventSlim(false);
        service.PaneStateChanged += (_, _) => signal.Set();

        await store.Dispatch(new NavToggleRequested("test"));
        Assert.True(signal.Wait(TimeSpan.FromSeconds(1)));
        var stabilizedIsPaneOpen = service.IsPaneOpen;

        var changedCount = 0;
        service.PaneStateChanged += (_, _) => changedCount++;

        await store.Dispatch(new WindowMetricsChanged(1280, 720, 1280, 720));

        Assert.Equal(stabilizedIsPaneOpen, service.IsPaneOpen);
        Assert.Equal(0, changedCount);
    }

    [Fact]
    public async Task Constructor_SeedsCurrentSnapshotImmediately()
    {
        await using var store = new TestShellLayoutStore(
            ShellLayoutState.Default with
            {
                WindowMetrics = new WindowMetrics(800, 720, 800, 720),
                UserNavOpenIntent = false
            });
        using var service = new NavigationStateService(store, new ImmediateUiDispatcher());

        Assert.False(service.IsPaneOpen);
    }

    [Fact]
    public async Task DisposePath_ShouldNotBlockAfterConstructorOnlyUse()
    {
        var store = new TestShellLayoutStore(
            ShellLayoutState.Default with
            {
                WindowMetrics = new WindowMetrics(800, 720, 800, 720),
                UserNavOpenIntent = false
            });
        var service = new NavigationStateService(store, new ImmediateUiDispatcher());

        Assert.False(service.IsPaneOpen);

        service.Dispose();

        var disposeTask = store.DisposeAsync().AsTask();
        var completedTask = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(disposeTask, completedTask);
        await disposeTask;
    }

    private sealed class TestShellLayoutStore : IShellLayoutStore, IAsyncDisposable
    {
        private readonly IState<ShellLayoutState> _state;
        private readonly IState<ShellLayoutSnapshot> _snapshot;

        public TestShellLayoutStore(ShellLayoutState? initialState = null)
        {
            CurrentState = initialState ?? ShellLayoutState.Default;
            CurrentSnapshot = ShellLayoutPolicy.Compute(CurrentState);
            _state = Uno.Extensions.Reactive.State.Value(new object(), () => CurrentState);
            _snapshot = Uno.Extensions.Reactive.State.Value(new object(), () => CurrentSnapshot);
        }

        public IFeed<ShellLayoutState> State => _state;
        public IFeed<ShellLayoutSnapshot> Snapshot => _snapshot;
        public ShellLayoutState CurrentState { get; private set; }
        public ShellLayoutSnapshot CurrentSnapshot { get; private set; }
        public event EventHandler<ShellLayoutChangedEventArgs>? Changed;

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

            CurrentState = reduced.State;
            CurrentSnapshot = reduced.Snapshot;
            await _snapshot.Update(_ => reduced.Snapshot, default);
            Changed?.Invoke(this, new ShellLayoutChangedEventArgs(CurrentState, CurrentSnapshot));
        }

        public async ValueTask DisposeAsync()
        {
            await _snapshot.DisposeAsync();
            await _state.DisposeAsync();
        }
    }
}
