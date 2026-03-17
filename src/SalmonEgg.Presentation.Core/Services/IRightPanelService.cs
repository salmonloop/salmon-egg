using System;

namespace SalmonEgg.Presentation.Services;

using SalmonEgg.Presentation.ViewModels.Navigation;

/// <summary>
/// Service for managing the state of the right sidebar (SSOT).
/// </summary>
public interface IRightPanelService
{
    RightPanelMode CurrentMode { get; set; }
    event EventHandler? ModeChanged;
}
