namespace SalmonEgg.Application.Common.Shell;

/// <summary>
/// UI policy for a shell navigation pane (e.g. WinUI/Uno NavigationView).
/// Keeps the pane open by default and prevents "mysterious" auto-collapses,
/// while still allowing an explicit user-requested close.
/// </summary>
public sealed class ShellPanePolicy
{
    private bool _allowNextClose;

    public bool DefaultIsOpen { get; } = true;

    /// <summary>
    /// Compute the next open state when the user toggles the pane.
    /// </summary>
    public bool Toggle(bool currentIsOpen)
    {
        var next = !currentIsOpen;
        if (!next)
        {
            // The next closing request is user-initiated; allow it once.
            _allowNextClose = true;
        }

        return next;
    }

    /// <summary>
    /// Decide whether a pane-closing request should be cancelled.
    /// </summary>
    /// <param name="isMinimalMode">
    /// If the shell is in minimal display mode, the pane may need to close (e.g. after selection)
    /// to keep the content usable on narrow windows; we allow closing in that case.
    /// </param>
    public bool ShouldCancelClosing(bool isMinimalMode)
    {
        if (isMinimalMode)
        {
            _allowNextClose = false;
            return false;
        }

        if (_allowNextClose)
        {
            _allowNextClose = false;
            return false;
        }

        return true;
    }
}
