using System;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// Decides which package-manager executable answers for a component: the one belonging to the toolchain
/// the user named, rather than whatever the inherited PATH happens to resolve.
/// </summary>
/// <remarks>
/// A package manager only ever answers for its own toolchain. Asking a different one is not a degraded
/// answer but a wrong one: a package installed under the toolchain the user named reads as absent, and a
/// package absent there can read as installed. Both mislead — the first makes the wizard offer an install
/// the user already did, the second makes it skip the install and fail at launch.
///
/// The manager needs no separate answer from the user because it is the launcher's sibling. npm publishes
/// <c>npm</c> and <c>npx</c> from one package into one bin directory, and the uv installer lays down
/// <c>uv</c> beside <c>uvx</c>; the Windows shims (<c>npm.cmd</c>, <c>npx.cmd</c>) sit there too. So an
/// override for the launcher already identifies the manager, and asking twice for one toolchain would be
/// a question the wizard can answer itself.
///
/// Derivation is a fallback, not a rule: an explicit override for the manager's own name wins, because
/// the derivation exists to spare the user a second answer rather than to overrule one they gave.
///
/// This type decides <em>which</em> command to use and performs no IO; whether the derived path exists is
/// the platform probe's question, since only it knows how this machine resolves executables.
/// </remarks>
public static class AcpPackageManagerCommand
{
    /// <summary>
    /// Resolves the package-manager command for <paramref name="distribution"/>, honouring
    /// <paramref name="overrides"/> for the manager itself and otherwise deriving it from the launcher.
    /// </summary>
    /// <param name="launcherCommand">
    /// The launcher the component is detected and started through, already resolved through
    /// <paramref name="overrides"/>. An absolute path names a toolchain; a bare name does not.
    /// </param>
    /// <returns>
    /// The manager's bare name when no toolchain is identified — so PATH resolution answers as before —
    /// and a toolchain-qualified candidate otherwise. A derived candidate is a preference the probe may
    /// fall back from; see <see cref="AcpPackageManagerCandidates"/>.
    /// </returns>
    public static AcpPackageManagerCandidates Resolve(
        AcpDistributionKind distribution,
        string? launcherCommand,
        AcpCommandOverrides? overrides)
    {
        var managerName = ResolveName(distribution);
        if (managerName.Length == 0)
        {
            return AcpPackageManagerCandidates.Exact(managerName);
        }

        // An answer the user gave outranks one derived on their behalf, so it is used as given.
        var overridden = (overrides ?? AcpCommandOverrides.Empty).Resolve(managerName);
        if (!string.Equals(overridden, managerName, StringComparison.Ordinal))
        {
            return AcpPackageManagerCandidates.Exact(overridden);
        }

        return ResolveSibling(launcherCommand, managerName) is { } sibling
            ? AcpPackageManagerCandidates.PreferredWithFallback(sibling, managerName)
            : AcpPackageManagerCandidates.Exact(managerName);
    }

    /// <summary>The command that lists global installs for <paramref name="distribution"/>.</summary>
    public static string ResolveName(AcpDistributionKind distribution)
        => distribution switch
        {
            AcpDistributionKind.Npx => "npm",
            AcpDistributionKind.Uvx => "uv",
            _ => string.Empty
        };

    /// <summary>
    /// Returns the manager path sitting next to <paramref name="launcherCommand"/>, or null when the
    /// launcher names no directory.
    /// </summary>
    /// <remarks>
    /// The launcher's own extension is carried over, which is what makes this work on Windows: npm
    /// installs its launchers as <c>.cmd</c> shims, so the sibling of <c>npx.cmd</c> is <c>npm.cmd</c>
    /// and not an extensionless <c>npm</c> that CreateProcess cannot start.
    ///
    /// Directory and extension are split by hand rather than through <c>System.IO.Path</c>: this decision
    /// belongs to the domain, which stays free of filesystem types, and the separators it must understand
    /// are exactly the two below on every platform the app targets.
    /// </remarks>
    private static string? ResolveSibling(string? launcherCommand, string managerName)
    {
        if (string.IsNullOrWhiteSpace(launcherCommand))
        {
            return null;
        }

        var launcher = launcherCommand.Trim();
        var separator = launcher.LastIndexOfAny(new[] { '/', '\\' });
        if (separator < 0)
        {
            // A bare name: PATH resolution is the only route, and it applies to the manager too.
            return null;
        }

        var directory = launcher[..(separator + 1)];
        var fileName = launcher[(separator + 1)..];
        if (fileName.Length == 0)
        {
            // A trailing separator names a directory, not a launcher.
            return null;
        }

        var dot = fileName.LastIndexOf('.');
        var extension = dot > 0 ? fileName[dot..] : string.Empty;
        return directory + managerName + extension;
    }
}
