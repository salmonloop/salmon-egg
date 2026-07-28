using System.Threading;
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
        var stateOwner = new object();
        var snapshotOwner = new object();
        await using var state = State.Value(stateOwner, () => initialState);
        await using var snapshot = State.Value(snapshotOwner, () => initialSnapshot);
        var store = new ShellLayoutStore(state, snapshot, initialState, initialSnapshot);

        var expected = ShellLayoutReducer.Reduce(ShellLayoutState.Default, new NavToggleRequested("Test")).Snapshot.IsNavPaneOpen;

        await store.Dispatch(new NavToggleRequested("Test"));

        Assert.Equal(expected, store.CurrentSnapshot.IsNavPaneOpen);

        var current = await WaitForSnapshotAsync(snapshot, value => value?.IsNavPaneOpen == expected);
        Assert.NotNull(current);
        Assert.Equal(expected, current!.IsNavPaneOpen);
        GC.KeepAlive(stateOwner);
        GC.KeepAlive(snapshotOwner);
    }

    [Fact]
    public async Task Dispatch_UnderConcurrency_SerializesAndKeepsCurrentStateConsistent()
    {
        // dispatch 门控:并发 dispatch 不得撕开 reduce → Current* → 快照 → Changed 的顺序。
        // N 次成对 toggle 后每次都应精确触发一次 Changed,且末态的 Current* 与快照 feed 一致收敛。
        var initialState = ShellLayoutState.Default;
        var initialSnapshot = ShellLayoutPolicy.Compute(initialState);
        var stateOwner = new object();
        var snapshotOwner = new object();
        await using var state = State.Value(stateOwner, () => initialState);
        await using var snapshot = State.Value(snapshotOwner, () => initialSnapshot);
        var store = new ShellLayoutStore(state, snapshot, initialState, initialSnapshot);

        var changedCount = 0;
        store.Changed += (_, _) => Interlocked.Increment(ref changedCount);

        const int pairs = 64;
        var tasks = new Task[pairs * 2];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(
                async () => await store.Dispatch(new NavToggleRequested("Concurrent")),
                TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);

        // 偶数次 toggle 回到初始开合态;门控保证不丢更新、不重复。
        Assert.Equal(tasks.Length, changedCount);
        Assert.Equal(initialSnapshot.IsNavPaneOpen, store.CurrentSnapshot.IsNavPaneOpen);
        Assert.Equal(store.CurrentSnapshot.IsNavPaneOpen, ShellLayoutPolicy.Compute(store.CurrentState).IsNavPaneOpen);

        var converged = await WaitForSnapshotAsync(
            snapshot,
            value => value?.IsNavPaneOpen == store.CurrentSnapshot.IsNavPaneOpen);
        Assert.NotNull(converged);
        Assert.Equal(store.CurrentSnapshot.IsNavPaneOpen, converged!.IsNavPaneOpen);
        GC.KeepAlive(stateOwner);
        GC.KeepAlive(snapshotOwner);
    }

    private static async Task<ShellLayoutSnapshot?> WaitForSnapshotAsync(
        IState<ShellLayoutSnapshot> snapshot,
        System.Func<ShellLayoutSnapshot?, bool> predicate,
        int maxAttempts = 200,
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
