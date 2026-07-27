using System;
using System.Threading;
using System.Threading.Tasks;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Mvux.ShellLayout;

public interface IShellLayoutStore
{
    IFeed<ShellLayoutState> State { get; }
    IFeed<ShellLayoutSnapshot> Snapshot { get; }
    ShellLayoutState CurrentState { get; }
    ShellLayoutSnapshot CurrentSnapshot { get; }
    event EventHandler<ShellLayoutChangedEventArgs>? Changed;
    ValueTask Dispatch(ShellLayoutAction action);
}

public sealed class ShellLayoutChangedEventArgs : EventArgs
{
    public ShellLayoutChangedEventArgs(ShellLayoutState state, ShellLayoutSnapshot snapshot)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public ShellLayoutState State { get; }

    public ShellLayoutSnapshot Snapshot { get; }
}

public sealed class ShellLayoutStore : IShellLayoutStore
{
    private readonly IState<ShellLayoutState> _state;
    private readonly IState<ShellLayoutSnapshot> _snapshotState;
    // 串行化整段 dispatch:reduce → 提交 Current* → 快照更新 → Changed 必须作为一个不可分单元,
    // 否则并发 dispatch 会交错,把 Current*、快照 feed 与 Changed 事件的先后撕开(对照 ChatStore)。
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    public IFeed<ShellLayoutState> State => _state;
    public IFeed<ShellLayoutSnapshot> Snapshot => _snapshotState;
    public ShellLayoutState CurrentState { get; private set; }
    public ShellLayoutSnapshot CurrentSnapshot { get; private set; }
    public event EventHandler<ShellLayoutChangedEventArgs>? Changed;

    public ShellLayoutStore(
        IState<ShellLayoutState> state,
        IState<ShellLayoutSnapshot> snapshotState,
        ShellLayoutState initialState,
        ShellLayoutSnapshot initialSnapshot)
    {
        _state = state;
        _snapshotState = snapshotState;
        CurrentState = initialState;
        CurrentSnapshot = initialSnapshot;
    }

    public async ValueTask Dispatch(ShellLayoutAction action)
    {
        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ShellLayoutReduced? reduced = null;

            await _state.Update(s =>
            {
                reduced = ShellLayoutReducer.Reduce(s!, action);
                return reduced.State;
            }, default).ConfigureAwait(false);

            if (reduced is null)
            {
                return;
            }

            CurrentState = reduced.State;
            CurrentSnapshot = reduced.Snapshot;
            await _snapshotState.Update(_ => reduced.Snapshot, default).ConfigureAwait(false);
            Changed?.Invoke(this, new ShellLayoutChangedEventArgs(CurrentState, CurrentSnapshot));
        }
        finally
        {
            _dispatchGate.Release();
        }
    }
}
