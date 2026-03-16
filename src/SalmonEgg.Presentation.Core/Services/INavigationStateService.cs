using System;

namespace SalmonEgg.Presentation.Services;

/// <summary>
/// A strict SSOT (Single Source of Truth) service for managing the global navigation pane state.
/// This replaces top-down state pushing and ensures all view models read from the same memory location.
/// </summary>
public interface INavigationStateService
{
    bool IsPaneOpen { get; set; }
    event EventHandler? PaneStateChanged;
}
