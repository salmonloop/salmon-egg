using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace SalmonEgg.Presentation.Services.Input;

/// <summary>
/// Single source of truth for main-shell focus scope primitives used by the
/// gamepad navigation / context / shortcut dispatchers. Consumers must obtain
/// the focused element, root content, and ancestor walk exclusively through
/// this service; direct <see cref="App.MainWindowInstance"/> +
/// <see cref="Microsoft.UI.Xaml.Input.FocusManager"/> +
/// <see cref="Microsoft.UI.Xaml.Media.VisualTreeHelper"/> plumbing must not
/// be re-implemented inside individual dispatchers.
/// </summary>
public interface IShellFocusScope
{
    /// <summary>Returns the currently focused element inside the main window XamlRoot, or null when the main window is not ready.</summary>
    DependencyObject? GetFocusedElement();

    /// <summary>Returns the current root content element. If the main window content is a <see cref="Microsoft.UI.Xaml.Controls.Frame"/>, its current content is returned instead.</summary>
    DependencyObject? GetCurrentRootContent();

    /// <summary>Enumerates the given element and its ancestors up the visual tree. Yields nothing when <paramref name="start"/> is null.</summary>
    IEnumerable<DependencyObject> EnumerateAncestors(DependencyObject? start);
}
