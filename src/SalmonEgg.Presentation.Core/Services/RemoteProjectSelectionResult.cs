namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// Outcome of the remote project selection dialog, expressed without any UI type so the
/// dialog stays a pure view. The owning command interprets the result: confirm feeds the
/// selected directory id into the unified add-project coordinator, manage navigates to the
/// authoritative remote-path settings, cancel changes nothing.
/// </summary>
public abstract record RemoteProjectSelectionResult
{
    private RemoteProjectSelectionResult()
    {
    }

    /// <summary>The user confirmed a remote directory selection.</summary>
    public sealed record Confirmed(string DirectoryId) : RemoteProjectSelectionResult;

    /// <summary>
    /// The user asked to manage remote paths (secondary button when directories exist, or
    /// "go to settings" in the empty state). Both route to the same settings destination.
    /// </summary>
    public sealed record ManageRequested : RemoteProjectSelectionResult;

    /// <summary>The dialog was cancelled or dismissed without a selection.</summary>
    public sealed record Cancelled : RemoteProjectSelectionResult;

    public static readonly RemoteProjectSelectionResult Manage = new ManageRequested();

    public static readonly RemoteProjectSelectionResult Cancel = new Cancelled();
}
