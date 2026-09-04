using System;

namespace SalmonEgg.Domain.Models.Cli;

/// <summary>
/// What a shell on this machine would reach when the user types the command name.
/// </summary>
/// <remarks>
/// Every field is observed rather than configured. The command's registration is owned by whichever
/// installer put the app on the machine, so nothing the app persists could describe it: an MSIX alias, a
/// PATH entry written by an MSI, a symlink owned by dpkg and a symlink left by a macOS installer all look
/// different, and any of them can be absent, stale, or pointing at another installation entirely.
/// </remarks>
public sealed record CliCommandRegistration
{
    private CliCommandRegistration(
        CliCommandRegistrationState state,
        string? resolvedPath,
        string? resolvedTargetPath,
        string? reportedVersion,
        string expectedVersion,
        string? failureDetail)
    {
        State = state;
        ResolvedPath = resolvedPath;
        ResolvedTargetPath = resolvedTargetPath;
        ReportedVersion = reportedVersion;
        ExpectedVersion = expectedVersion;
        FailureDetail = failureDetail;
    }

    public CliCommandRegistrationState State { get; }

    /// <summary>The path PATH resolution produced, or <c>null</c> when nothing matched.</summary>
    public string? ResolvedPath { get; }

    /// <summary>
    /// Where that path leads once symlinks are followed, when it differs from <see cref="ResolvedPath"/>.
    /// Worth surfacing on its own: on Linux and macOS the entry on PATH is a link, and a link pointing at
    /// an app bundle the user has since replaced is exactly how a stale command survives.
    /// </summary>
    public string? ResolvedTargetPath { get; }

    /// <summary>The version the resolved executable reported, or <c>null</c> when it did not say.</summary>
    public string? ReportedVersion { get; }

    /// <summary>The version this app expects, which is its own.</summary>
    public string ExpectedVersion { get; }

    /// <summary>Why the version could not be read, for <see cref="CliCommandRegistrationState.Unreadable"/>.</summary>
    public string? FailureDetail { get; }

    public static CliCommandRegistration Unsupported(string expectedVersion) =>
        new(CliCommandRegistrationState.Unsupported, null, null, null, expectedVersion, null);

    public static CliCommandRegistration NotRegistered(string expectedVersion) =>
        new(CliCommandRegistrationState.NotRegistered, null, null, null, expectedVersion, null);

    /// <summary>
    /// Classifies a resolved command by comparing versions. The comparison is the caller's whole reason for
    /// starting a process, so it lives here rather than at the call site: a mismatch and a match differ only
    /// in this one decision, and duplicating it is how the two states drift apart.
    /// </summary>
    public static CliCommandRegistration Resolved(
        string resolvedPath,
        string? resolvedTargetPath,
        string reportedVersion,
        string expectedVersion)
    {
        var state = VersionsMatch(reportedVersion, expectedVersion)
            ? CliCommandRegistrationState.Registered
            : CliCommandRegistrationState.VersionMismatch;

        return new(state, resolvedPath, resolvedTargetPath, reportedVersion, expectedVersion, null);
    }

    public static CliCommandRegistration Unreadable(
        string resolvedPath,
        string? resolvedTargetPath,
        string expectedVersion,
        string failureDetail) =>
        new(CliCommandRegistrationState.Unreadable, resolvedPath, resolvedTargetPath, null, expectedVersion, failureDetail);

    /// <summary>
    /// Compares the release identity of two version strings.
    /// </summary>
    /// <remarks>
    /// The CLI reports its informational version, which carries a prerelease label and a commit hash
    /// (<c>1.4.3-alpha.0.47+abc123</c>), while the app knows its own assembly version
    /// (<c>1.4.3.0</c>). Comparing those verbatim would report a mismatch for every development build of a
    /// matched pair, so only the leading three numeric components are compared: that is the release identity
    /// MinVer derives from the tag, and it is what "the same release" means for a user asking whether their
    /// command is current.
    /// </remarks>
    private static bool VersionsMatch(string reported, string expected) =>
        string.Equals(ReleaseIdentity(reported), ReleaseIdentity(expected), StringComparison.Ordinal);

    private static string ReleaseIdentity(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var span = version.AsSpan().Trim();
        var cut = span.IndexOfAny('-', '+');
        if (cut >= 0)
        {
            span = span[..cut];
        }

        var parts = span.ToString().Split('.');
        return parts.Length >= 3
            ? string.Join('.', parts[0], parts[1], parts[2])
            : span.ToString();
    }
}
