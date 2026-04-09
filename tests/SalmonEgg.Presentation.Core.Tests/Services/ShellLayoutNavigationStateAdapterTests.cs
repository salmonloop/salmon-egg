using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
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
        using var adapter = new ShellLayoutNavigationStateAdapter(store);

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
        using var adapter = new ShellLayoutNavigationStateAdapter(store);

        Assert.False(adapter.IsPaneOpen);
    }

    [Fact]
    public async Task Adapter_RaisesPaneStateChangedOnCapturedSynchronizationContext()
    {
        var syncContext = new QueuedSynchronizationContext();
        await using var store = new TestShellLayoutStore();
        using var adapter = new ShellLayoutNavigationStateAdapter(store, syncContext);
        var changedCount = 0;
        var raisedOnCapturedContext = false;
        adapter.PaneStateChanged += (_, _) =>
        {
            changedCount++;
            raisedOnCapturedContext = ReferenceEquals(SynchronizationContext.Current, syncContext);
        };

        await store.SetPaneOpenAsync(false);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (syncContext.PendingPostCount == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(0, changedCount);
        syncContext.DrainAll();
        Assert.Equal(1, changedCount);
        Assert.True(raisedOnCapturedContext);
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

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int PendingPostCount
        {
            get
            {
                lock (_queue)
                {
                    return _queue.Count;
                }
            }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue)
            {
                _queue.Enqueue((d, state));
            }
        }

        public void DrainAll(int maxIterations = 32)
        {
            for (var i = 0; i < maxIterations; i++)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                    {
                        return;
                    }

                    item = _queue.Dequeue();
                }

                var previous = Current;
                SetSynchronizationContext(this);
                try
                {
                    item.Callback(item.State);
                }
                finally
                {
                    SetSynchronizationContext(previous);
                }
            }

            throw new InvalidOperationException("SynchronizationContext queue did not drain.");
        }
    }
}
