using System;

namespace SalmonEgg.Presentation.Services;

public sealed class NavigationStateService : INavigationStateService
{
    private bool _isPaneOpen = true; // Match the default expanded state

    public bool IsPaneOpen
    {
        get => _isPaneOpen;
        set
        {
            if (_isPaneOpen != value)
            {
                _isPaneOpen = value;
                PaneStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? PaneStateChanged;
}
