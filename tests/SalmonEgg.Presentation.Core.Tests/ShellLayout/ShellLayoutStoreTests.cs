using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.ShellLayout;

public sealed class ShellLayoutStoreTests
{
    [Fact]
    public async Task Dispatch_Updates_Snapshot_After_Toggle()
    {
        var initialState = ShellLayoutState.Default;
        var initialSnapshot = ShellLayoutPolicy.Compute(initialState);
        await using var state = State.Value(new object(), () => initialState);
        await using var snapshot = State.Value(new object(), () => initialSnapshot);
        var store = new ShellLayoutStore(state, snapshot, initialState, initialSnapshot);

        var expected = ShellLayoutReducer.Reduce(ShellLayoutState.Default, new NavToggleRequested("Test")).Snapshot.IsNavPaneOpen;

        await store.Dispatch(new NavToggleRequested("Test"));

        var current = await snapshot;
        Assert.NotNull(current);
        Assert.Equal(expected, current!.IsNavPaneOpen);
    }
}
