using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Services;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public class NavigationStateServiceTests
{
    [Fact]
    public async Task IsPaneOpen_ShouldChangeStateAndNotify()
    {
        using var store = new TestShellLayoutStore();
        using var service = new NavigationStateService(store);
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
        using var store = new TestShellLayoutStore();
        using var service = new NavigationStateService(store);
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
            _snapshot.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _state.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
