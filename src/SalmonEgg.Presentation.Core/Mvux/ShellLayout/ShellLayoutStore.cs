using System.Threading.Tasks;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Mvux.ShellLayout;

public interface IShellLayoutStore
{
    IState<ShellLayoutState> InternalState { get; }
    IState<ShellLayoutSnapshot> SnapshotState { get; }
    ValueTask Dispatch(ShellLayoutAction action);
}

public sealed class ShellLayoutStore : IShellLayoutStore
{
    private readonly IState<ShellLayoutSnapshot> _snapshotState;
    public IState<ShellLayoutState> InternalState { get; }
    public IState<ShellLayoutSnapshot> SnapshotState => _snapshotState;

    public ShellLayoutStore(IState<ShellLayoutState> state, IState<ShellLayoutSnapshot> snapshotState)
    {
        InternalState = state;
        _snapshotState = snapshotState;
    }

    public async ValueTask Dispatch(ShellLayoutAction action)
    {
        await InternalState.Update(s =>
        {
            var reduced = ShellLayoutReducer.Reduce(s!, action);
            _snapshotState.Update(_ => reduced.Snapshot, default);
            return reduced.State;
        }, default);
    }
}
