using System;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;

namespace SalmonEgg.Presentation.Services;

public sealed class RightPanelService : IRightPanelService, IDisposable
{
    private readonly IShellLayoutStore _store;
    private RightPanelMode _currentMode;
    private double _panelWidth;

    public RightPanelMode CurrentMode => _currentMode;

    public event EventHandler? ModeChanged;

    public double PanelWidth => _panelWidth;

    public event EventHandler? WidthChanged;

    public RightPanelService(IShellLayoutStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _currentMode = store.CurrentSnapshot.RightPanelMode;
        _panelWidth = store.CurrentSnapshot.RightPanelWidth;
        _store.Changed += OnShellLayoutChanged;
    }

    public void Dispose()
    {
        _store.Changed -= OnShellLayoutChanged;
    }

    private void OnShellLayoutChanged(object? sender, ShellLayoutChangedEventArgs e)
    {
        var snapshot = e.Snapshot;
        if (_currentMode != snapshot.RightPanelMode)
        {
            _currentMode = snapshot.RightPanelMode;
            ModeChanged?.Invoke(this, EventArgs.Empty);
        }

        if (!double.Equals(_panelWidth, snapshot.RightPanelWidth))
        {
            _panelWidth = snapshot.RightPanelWidth;
            WidthChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
