using System;
using System.Threading;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Services;

public sealed class NavigationStateService : INavigationStateService, IDisposable
{
    private readonly IDisposable? _subscription;
    private readonly IState<ShellLayoutSnapshot>? _snapshotState;
    private readonly SynchronizationContext? _syncContext;
    private bool _isPaneOpen;

    public bool IsPaneOpen => _isPaneOpen;

    public event EventHandler? PaneStateChanged;

    public NavigationStateService(IShellLayoutStore store, SynchronizationContext? syncContext = null)
    {
        _syncContext = syncContext ?? SynchronizationContext.Current;
        _isPaneOpen = store.CurrentSnapshot.IsNavPaneOpen;
        _snapshotState = State.FromFeed(this, store.Snapshot);
        _snapshotState.ForEach(async (snapshot, ct) =>
        {
            if (snapshot is null) return;
            if (_isPaneOpen == snapshot.IsNavPaneOpen) return;

            _isPaneOpen = snapshot.IsNavPaneOpen;
            RaisePaneStateChanged();
        }, out _subscription);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }

    private void RaisePaneStateChanged()
    {
        if (_syncContext is null || SynchronizationContext.Current == _syncContext)
        {
            PaneStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _syncContext.Post(_ => PaneStateChanged?.Invoke(this, EventArgs.Empty), null);
    }
}
