using System;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using Uno.Extensions.Reactive;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Services;

public sealed class NavigationStateService : INavigationStateService, IDisposable
{
    private readonly IShellLayoutMetricsSink _sink;
    private readonly IDisposable? _subscription;
    private bool _isPaneOpen = true;

    public bool IsPaneOpen
    {
        get => _isPaneOpen;
        set
        {
            if (_isPaneOpen != value)
            {
                _sink.ReportNavToggle("ServiceOverride");
            }
        }
    }

    public event EventHandler? PaneStateChanged;

    public NavigationStateService(IShellLayoutStore store, IShellLayoutMetricsSink sink)
    {
        _sink = sink;
        // Using the ForEach overload that worked in ShellLayoutViewModel
        store.SnapshotState.ForEach(async (snapshot, ct) =>
        {
            if (snapshot is null) return;
            if (_isPaneOpen == snapshot.IsNavPaneOpen) return;

            _isPaneOpen = snapshot.IsNavPaneOpen;
            PaneStateChanged?.Invoke(this, EventArgs.Empty);
        }, out _subscription);
    }

    public void Dispose() => _subscription?.Dispose();
}
