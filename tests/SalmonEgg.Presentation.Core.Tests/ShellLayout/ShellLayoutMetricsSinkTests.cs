using System.Threading.Tasks;
using Xunit;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Tests.ShellLayout;

public class ShellLayoutMetricsSinkTests
{
    [Fact]
    public async Task MetricsSink_Dispatches_WindowMetrics()
    {
        await using var store = new CapturingStore();
        var sink = new ShellLayoutMetricsSink(store);
        await sink.ReportWindowMetrics(100, 200, 80, 160);
        Assert.IsType<WindowMetricsChanged>(store.LastAction);
        var action = (WindowMetricsChanged)store.LastAction!;
        Assert.Equal(100, action.Width);
    }

    private sealed class CapturingStore : IShellLayoutStore, IAsyncDisposable
    {
        private readonly IState<ShellLayoutState> _state = Uno.Extensions.Reactive.State.Value(new object(), () => ShellLayoutState.Default);
        public IState<ShellLayoutSnapshot> SnapshotState { get; } = Uno.Extensions.Reactive.State.Value(new object(), () => ShellLayoutPolicy.Compute(ShellLayoutState.Default));
        public IFeed<ShellLayoutState> State => _state;
        public IFeed<ShellLayoutSnapshot> Snapshot => SnapshotState;
        public ShellLayoutAction? LastAction { get; private set; }
        public ValueTask Dispatch(ShellLayoutAction action) { LastAction = action; return ValueTask.CompletedTask; }

        public async ValueTask DisposeAsync()
        {
            await SnapshotState.DisposeAsync();
            await _state.DisposeAsync();
        }
    }
}
