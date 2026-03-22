using System;
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

        public async ValueTask DisposeAsync()
        {
            await SnapshotState.DisposeAsync();
            await _state.DisposeAsync();
        }
    }
}
