namespace SalmonEgg.Application.Common.Shell;

/// <summary>
/// Pure UI policy for a shell navigation pane (e.g. WinUI/Uno NavigationView).
/// The View should not maintain its own mutable "allow close once" state; it should
/// only reflect the current shell snapshot produced by the SSOT store.
/// </summary>
public static class ShellPanePolicy
{
    /// <summary>
     /// Decide whether a pane-closing request should be cancelled.
     /// </summary>
    /// <param name="desiredPaneOpen">
    /// The open/close intent projected from the shell store.
    /// </param>
    /// <param name="isExpandedMode">
    /// Whether the shell is currently in expanded mode.
    /// In compact/minimal modes the pane should be allowed to close so the app can honor
    /// light-dismiss and narrow-window interaction patterns.
     /// </param>
    public static bool ShouldCancelClosing(bool desiredPaneOpen, bool isExpandedMode)
        => isExpandedMode && desiredPaneOpen;
}
