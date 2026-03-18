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
    /// <param name="isMinimalMode">
     /// If the shell is in minimal display mode, the pane may need to close (e.g. after selection)
     /// to keep the content usable on narrow windows; we allow closing in that case.
     /// </param>
    public static bool ShouldCancelClosing(bool desiredPaneOpen, bool isMinimalMode)
        => !isMinimalMode && desiredPaneOpen;
}
