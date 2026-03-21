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

    private sealed class TestShellLayoutStore : IShellLayoutStore, IAsyncDisposable
    {
        private readonly IState<ShellLayoutState> _state;
        public IState<ShellLayoutSnapshot> SnapshotState { get; }

        public TestShellLayoutStore()
        {
            _state = Uno.Extensions.Reactive.State.Value(new object(), () => ShellLayoutState.Default);
            SnapshotState = Uno.Extensions.Reactive.State.Value(new object(), () => ShellLayoutPolicy.Compute(ShellLayoutState.Default));
        }

        public IFeed<ShellLayoutState> State => _state;
        public IFeed<ShellLayoutSnapshot> Snapshot => SnapshotState;

        public ValueTask Dispatch(ShellLayoutAction action) => ValueTask.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            await SnapshotState.DisposeAsync();
            await _state.DisposeAsync();
        }
    }
}
