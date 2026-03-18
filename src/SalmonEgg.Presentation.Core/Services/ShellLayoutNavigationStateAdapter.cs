using System;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Services;

public sealed class ShellLayoutNavigationStateAdapter : INavigationPaneState, IDisposable
{
    private readonly IDisposable? _subscription;
    private bool _isPaneOpen;
    public bool IsPaneOpen => _isPaneOpen;
    public event EventHandler? PaneStateChanged;

    public ShellLayoutNavigationStateAdapter(IShellLayoutStore store)
    {
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
