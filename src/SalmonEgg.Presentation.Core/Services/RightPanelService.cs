using System;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Services;

public sealed class RightPanelService : IRightPanelService
{
    private RightPanelMode _currentMode = RightPanelMode.None;

    public RightPanelMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (_currentMode != value)
            {
                _currentMode = value;
                ModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? ModeChanged;
}
