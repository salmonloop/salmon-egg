using System;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public class ShellLayoutNavigationStateAdapterTests
{
    [Fact]
    public async Task Adapter_SeedsInitialPaneStateFromSnapshot()
    {
        await using var store = new TestShellLayoutStore();
        using var adapter = new ShellLayoutNavigationStateAdapter(store, new ImmediateUiDispatcher());

        Assert.True(adapter.IsPaneOpen);
    }

    [Fact]
    public async Task Adapter_UsesCurrentSnapshotDuringConstruction()
    {
        await using var store = new TestShellLayoutStore(
            ShellLayoutState.Default with
            {
                WindowMetrics = new WindowMetrics(800, 720, 800, 720),
                UserNavOpenIntent = false
            });
        using var adapter = new ShellLayoutNavigationStateAdapter(store, new ImmediateUiDispatcher());

        Assert.False(adapter.IsPaneOpen);
    }

    [Fact]
    public async Task Adapter_RaisesPaneStateChangedWhenQueuedDispatcherRuns()
    {
        var dispatcher = new QueueingUiDispatcher();
        await using var store = new TestShellLayoutStore();
        using var adapter = new ShellLayoutNavigationStateAdapter(store, dispatcher);
        var changedCount = 0;
        adapter.PaneStateChanged += (_, _) => changedCount++;

        await store.SetPaneOpenAsync(false);
        await WaitForConditionAsync(() => adapter.IsPaneOpen == false);
        await WaitForConditionAsync(() => dispatcher.PendingCount == 1);

        Assert.Equal(0, changedCount);
        dispatcher.RunAll();
        Assert.Equal(1, changedCount);
        Assert.False(adapter.IsPaneOpen);
    }

    private sealed class TestShellLayoutStore : IShellLayoutStore, IAsyncDisposable
    {
        private readonly IState<ShellLayoutState> _state;
        public IState<ShellLayoutSnapshot> SnapshotState { get; }

        public TestShellLayoutStore(ShellLayoutState? initialState = null)
        {
            CurrentState = initialState ?? ShellLayoutState.Default;
            CurrentSnapshot = ShellLayoutPolicy.Compute(CurrentState);
            _state = Uno.Extensions.Reactive.State.Value(new object(), () => CurrentState);
            SnapshotState = Uno.Extensions.Reactive.State.Value(new object(), () => CurrentSnapshot);
        }

        public IFeed<ShellLayoutState> State => _state;
        public IFeed<ShellLayoutSnapshot> Snapshot => SnapshotState;
        public ShellLayoutState CurrentState { get; private set; }
        public ShellLayoutSnapshot CurrentSnapshot { get; private set; }

        public ValueTask Dispatch(ShellLayoutAction action) => ValueTask.CompletedTask;

        public async ValueTask SetPaneOpenAsync(bool isPaneOpen)
        {
            CurrentState = CurrentState with
            {
                WindowMetrics = new WindowMetrics(800, 720, 800, 720),
                UserNavOpenIntent = isPaneOpen,
                IsMinimalPaneOpen = isPaneOpen
            };
            CurrentSnapshot = ShellLayoutPolicy.Compute(CurrentState);
            await SnapshotState.Update(_ => CurrentSnapshot, default);
        }

        public async ValueTask DisposeAsync()
        {
            await SnapshotState.DisposeAsync();
            await _state.DisposeAsync();
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, int attempts = 20, int delayMs = 10)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(delayMs);
        }
    }
}
