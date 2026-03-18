using System;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Services;

public sealed class RightPanelService : IRightPanelService, IDisposable
{
    private readonly IShellLayoutMetricsSink _sink;
    private readonly IDisposable? _subscription;
    private RightPanelMode _currentMode = RightPanelMode.None;
    private double _panelWidth = 320;

    public RightPanelMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (_currentMode != value)
            {
                _sink.ReportRightPanelMode(value);
            }
        }
    }

    public event EventHandler? ModeChanged;

    public double PanelWidth
    {
        get => _panelWidth;
        set
        {
            if (!double.Equals(_panelWidth, value))
            {
                _sink.ReportRightPanelWidth(value);
            }
        }
    }

    public event EventHandler? WidthChanged;

    public RightPanelService(IShellLayoutStore store, IShellLayoutMetricsSink sink)
    {
        _sink = sink;
        // Using the ForEach overload that worked in ShellLayoutViewModel
        store.SnapshotState.ForEach(async (snapshot, ct) =>
        {
            if (snapshot is null) return;

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
        }, out _subscription);
    }

    public void Dispose() => _subscription?.Dispose();
}
