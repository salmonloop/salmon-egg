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

        Assert.Equal(expected, store.CurrentSnapshot.IsNavPaneOpen);

        var current = await WaitForSnapshotAsync(snapshot, value => value?.IsNavPaneOpen == expected);
        Assert.NotNull(current);
        Assert.Equal(expected, current!.IsNavPaneOpen);
    }

    private static async Task<ShellLayoutSnapshot?> WaitForSnapshotAsync(
        IState<ShellLayoutSnapshot> snapshot,
        System.Func<ShellLayoutSnapshot?, bool> predicate,
        int maxAttempts = 20,
        int delayMs = 10)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var current = await snapshot;
            if (predicate(current))
            {
                return current;
            }

            await Task.Delay(delayMs);
        }

        return await snapshot;
    }
}
