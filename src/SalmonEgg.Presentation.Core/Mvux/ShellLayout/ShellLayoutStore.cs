using System.Threading.Tasks;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Mvux.ShellLayout;

public interface IShellLayoutStore
{
    IFeed<ShellLayoutState> State { get; }
    IFeed<ShellLayoutSnapshot> Snapshot { get; }
    ShellLayoutState CurrentState { get; }
    ShellLayoutSnapshot CurrentSnapshot { get; }
    ValueTask Dispatch(ShellLayoutAction action);
}

public sealed class ShellLayoutStore : IShellLayoutStore
{
    private readonly IState<ShellLayoutState> _state;
    private readonly IState<ShellLayoutSnapshot> _snapshotState;
    public IFeed<ShellLayoutState> State => _state;
    public IFeed<ShellLayoutSnapshot> Snapshot => _snapshotState;
    public ShellLayoutState CurrentState { get; private set; }
    public ShellLayoutSnapshot CurrentSnapshot { get; private set; }

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
        ShellLayoutReduced? reduced = null;

        await _state.Update(s =>
        {
            reduced = ShellLayoutReducer.Reduce(s!, action);
            return reduced.State;
        }, default);

        if (reduced is null)
        {
            return;
        }

        CurrentState = reduced.State;
        CurrentSnapshot = reduced.Snapshot;
        await _snapshotState.Update(_ => reduced.Snapshot, default);
    }
}
