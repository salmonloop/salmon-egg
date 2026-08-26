using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// Where one toolchain installer places the executables it manages, expressed relative to a base
/// directory with at most one wildcard segment standing for a version.
/// </summary>
/// <remarks>
/// Scanning these is what makes <em>several</em> installed versions visible. A version manager puts only
/// the version it has activated on PATH, so asking the user's shell — which is the only way to learn what
/// nvm activated — reports exactly one node however many are installed. The wizard needs the rest to tell
/// a user their agent exists under a version they are not currently using, which is otherwise
/// indistinguishable from not having it at all.
///
/// It is also the only route on Windows, where there is no profile-built PATH to capture.
///
/// Layouts are declared as data rather than probed for, because the alternative is walking a user's home
/// directory looking for anything that resembles a toolchain — unbounded IO for a question that a fixed,
/// documented list answers. A layout naming a directory that does not exist simply contributes nothing.
/// </remarks>
public sealed class AcpToolchainLayout
{
    /// <summary>The wildcard segment standing for a version directory.</summary>
    public const string VersionWildcard = "*";

    private AcpToolchainLayout(AcpToolchainLayoutRoot root, IReadOnlyList<string> segments)
    {
        Root = root;
        Segments = segments;
    }

    /// <summary>Which base directory <see cref="Segments"/> is relative to.</summary>
    public AcpToolchainLayoutRoot Root { get; }

    /// <summary>
    /// Path segments below <see cref="Root"/>, at most one of which is <see cref="VersionWildcard"/>.
    /// </summary>
    public IReadOnlyList<string> Segments { get; }

    /// <summary>True when this layout expands to one directory per installed version.</summary>
    public bool IsVersioned
    {
        get
        {
            foreach (var segment in Segments)
            {
                if (string.Equals(segment, VersionWildcard, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Declares a layout: <paramref name="segments"/> below <paramref name="root"/>, at most one of them
    /// being <see cref="VersionWildcard"/>.
    /// </summary>
    /// <remarks>
    /// Public because the set of layouts is configuration rather than a closed rule — a deployment that
    /// standardizes on a toolchain location this list does not know can supply its own — and because
    /// <see cref="Known"/> is then just the shipped default rather than a privileged code path.
    /// </remarks>
    public static AcpToolchainLayout Create(AcpToolchainLayoutRoot root, params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var wildcards = 0;
        foreach (var segment in segments)
        {
            if (string.Equals(segment, VersionWildcard, StringComparison.Ordinal))
            {
                wildcards++;
            }
        }

        if (wildcards > 1)
        {
            // Expansion resolves exactly one version directory. Rejecting this here turns a layout the
            // scan cannot honour into an authoring error rather than silently-missing directories.
            throw new ArgumentException(
                $"A layout may declare at most one '{VersionWildcard}' segment.",
                nameof(segments));
        }

        return new AcpToolchainLayout(root, segments);
    }

    /// <summary>
    /// The layouts the wizard scans, in the order they are searched.
    /// </summary>
    /// <remarks>
    /// Ordered so a version manager outranks a system-wide directory. That mirrors what a user's own shell
    /// does — a manager prepends its directory to PATH — so the wizard's first candidate matches the one
    /// their terminal would run.
    ///
    /// Only managers that publish a stable, documented layout are listed. asdf and mise are represented by
    /// their shim directories rather than per-version paths: both resolve versions through a shim that
    /// reads configuration at invocation time, so the shim is the honest entry point and enumerating
    /// versions behind it would name executables that refuse to run outside a configured directory.
    /// </remarks>
    public static IReadOnlyList<AcpToolchainLayout> Known { get; } = new[]
    {
        // Node version managers, per-version bin directories.
        Create(AcpToolchainLayoutRoot.UserHome, ".nvm", "versions", "node", VersionWildcard, "bin"),
        Create(AcpToolchainLayoutRoot.UserHome, ".fnm", "node-versions", VersionWildcard, "installation", "bin"),
        Create(AcpToolchainLayoutRoot.UserHome, ".local", "share", "fnm", "node-versions", VersionWildcard, "installation", "bin"),
        Create(AcpToolchainLayoutRoot.UserHome, ".nodenv", "versions", VersionWildcard, "bin"),
        Create(AcpToolchainLayoutRoot.UserHome, ".n", "n", "versions", "node", VersionWildcard, "bin"),

        // Managers that front every version behind one shim directory.
        Create(AcpToolchainLayoutRoot.UserHome, ".volta", "bin"),
        Create(AcpToolchainLayoutRoot.UserHome, ".asdf", "shims"),
        Create(AcpToolchainLayoutRoot.UserHome, ".local", "share", "mise", "shims"),

        // Per-user install directories that a GUI session commonly lacks.
        Create(AcpToolchainLayoutRoot.UserHome, ".local", "bin"),
        Create(AcpToolchainLayoutRoot.UserHome, ".bun", "bin"),
        Create(AcpToolchainLayoutRoot.UserHome, ".deno", "bin"),
        Create(AcpToolchainLayoutRoot.UserHome, ".cargo", "bin"),

        // npm's own global prefix on Windows, which its installer does not always add to PATH.
        Create(AcpToolchainLayoutRoot.WindowsRoamingAppData, "npm"),

        // Homebrew, whose prefix differs by architecture and is absent from a Finder-launched PATH.
        Create(AcpToolchainLayoutRoot.Absolute, "/opt/homebrew/bin"),
        Create(AcpToolchainLayoutRoot.Absolute, "/usr/local/bin")
    };
}
