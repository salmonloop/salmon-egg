namespace SalmonEgg.Domain.Models.Cli;

/// <summary>
/// The result of the app itself creating or removing the command's entry on PATH.
/// </summary>
/// <remarks>
/// Only one platform reaches this type. Windows and Linux installers own the registration, and a second
/// owner writing the same entry is how an uninstall ends up leaving a broken command behind; macOS is the
/// exception because a dragged .app has no install hook at all, so without this the .dmg's users have no
/// path to the command.
/// </remarks>
public enum CliCommandLinkOutcome
{
    /// <summary>The link now points at this app's bundled command.</summary>
    Linked,

    /// <summary>The link is gone.</summary>
    Unlinked,

    /// <summary>The user dismissed the authorization prompt. Not a failure: nothing was changed.</summary>
    Cancelled,

    /// <summary>The operation ran and failed. <see cref="CliCommandLinkResult.Detail"/> says how.</summary>
    Failed,

    /// <summary>This platform does not let the app own the registration.</summary>
    Unsupported,
}

/// <summary>
/// The outcome of a link operation, with the detail a user needs when it did not work.
/// </summary>
public sealed record CliCommandLinkResult(CliCommandLinkOutcome Outcome, string? Detail = null)
{
    public static CliCommandLinkResult Linked() => new(CliCommandLinkOutcome.Linked);

    public static CliCommandLinkResult Unlinked() => new(CliCommandLinkOutcome.Unlinked);

    public static CliCommandLinkResult Cancelled() => new(CliCommandLinkOutcome.Cancelled);

    public static CliCommandLinkResult Failed(string detail) => new(CliCommandLinkOutcome.Failed, detail);

    public static CliCommandLinkResult Unsupported() => new(CliCommandLinkOutcome.Unsupported);
}
