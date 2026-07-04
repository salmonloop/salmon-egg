using System;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Services;

public sealed class NavigationStateService : INavigationStateService, IDisposable
{
    private readonly IShellLayoutStore _store;
    private readonly IUiDispatcher _uiDispatcher;
    private bool _isPaneOpen;

    public bool IsPaneOpen => _isPaneOpen;

    public event EventHandler? PaneStateChanged;

    public NavigationStateService(IShellLayoutStore store, IUiDispatcher uiDispatcher)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _isPaneOpen = store.CurrentSnapshot.IsNavPaneOpen;
        _store.Changed += OnShellLayoutChanged;
    }

    public void Dispose()
    {
        _store.Changed -= OnShellLayoutChanged;
    }

    private void OnShellLayoutChanged(object? sender, ShellLayoutChangedEventArgs e)
    {
        if (_isPaneOpen == e.Snapshot.IsNavPaneOpen)
        {
            return;
        }

        _isPaneOpen = e.Snapshot.IsNavPaneOpen;
        RaisePaneStateChanged();
    }

    private void RaisePaneStateChanged()
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            PaneStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _uiDispatcher.Enqueue(() => PaneStateChanged?.Invoke(this, EventArgs.Empty));
    }
}
