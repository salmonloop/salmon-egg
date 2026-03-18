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
        await using var state = State.Value(new object(), () => ShellLayoutState.Default);
        await using var snapshot = State.Value(new object(), () => ShellLayoutPolicy.Compute(ShellLayoutState.Default));
        var store = new ShellLayoutStore(state, snapshot);

        await store.Dispatch(new NavToggleRequested("Test"));

        var current = await snapshot;
        Assert.NotNull(current);
        Assert.False(current!.IsNavPaneOpen);
    }
}
