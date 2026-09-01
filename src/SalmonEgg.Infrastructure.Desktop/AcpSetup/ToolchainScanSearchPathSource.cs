using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Contributes the bin directories of toolchains installed on disk, including versions that are not
/// currently active.
/// </summary>
/// <remarks>
/// This is what makes several installed versions visible. A version manager puts only the version it has
/// activated on PATH, so capturing the user's shell environment reports exactly one node however many are
/// installed — and the wizard needs the rest to tell a user their agent exists under a version they are
/// not currently using, which is otherwise indistinguishable from not having it.
///
/// It is also the only widening available on Windows, where there is no profile-built PATH to recover.
///
/// Scanned once and reused: the answer is a filesystem shape that does not change while the wizard runs,
/// and the wizard probes many components.
/// </remarks>
public sealed class ToolchainScanSearchPathSource : IAcpSearchPathSource
{
    /// <summary>
    /// Directory under the app data root that holds installed toolchains.
    /// </summary>
    /// <remarks>
    /// Shared with the toolchain installer, which writes here. A second spelling would let the scan look
    /// somewhere the installer never writes, so an install would succeed and stay undiscoverable.
    /// </remarks>
    internal const string ToolchainsDirectoryName = "toolchains";

    private readonly IReadOnlyList<AcpToolchainLayout> _layouts;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile IReadOnlyList<string>? _cached;

    public ToolchainScanSearchPathSource(IReadOnlyList<AcpToolchainLayout>? layouts = null)
    {
        _layouts = layouts ?? AcpToolchainLayout.Known;
    }

    public async Task<IReadOnlyList<string>> GetSearchDirectoriesAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cached is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is { } existing)
            {
                return existing;
            }

            // Returned from the local rather than by re-reading the field: an Invalidate arriving between the
            // two would otherwise make this return null, which callers are promised never happens.
            var scanned = Scan(cancellationToken);
            _cached = scanned;
            return scanned;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Drops the scan, so the next call walks the filesystem again.
    /// </summary>
    /// <remarks>
    /// Cleared without taking the gate: a scan already in flight will overwrite this with its own result,
    /// which is a scan of the machine as it was during that call rather than a stale one, and taking the
    /// gate would make invalidation wait on the very scan it wants replaced.
    /// </remarks>
    public void Invalidate() => _cached = null;

    /// <summary>
    /// Expands every layout to the directories that exist, in layout order.
    /// </summary>
    /// <remarks>
    /// Version directories within one layout are sorted descending by name, so a newer version is offered
    /// before an older one. That is a presentation choice rather than a claim about correctness: the
    /// wizard shows candidates and the user picks, but the newest install is the likelier intent and
    /// putting it first spares them reading a list to find it.
    /// </remarks>
    private IReadOnlyList<string> Scan(CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var layout in _layouts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var directory in ExpandLayout(layout))
            {
                if (seen.Add(directory))
                {
                    directories.Add(directory);
                }
            }
        }

        return directories;
    }

    private static IEnumerable<string> ExpandLayout(AcpToolchainLayout layout)
    {
        if (ResolveRoot(layout.Root) is not { } root)
        {
            return Array.Empty<string>();
        }

        return layout.IsVersioned
            ? ExpandVersioned(root, layout.Segments)
            : ExpandFixed(root, layout.Segments);
    }

    /// <summary>
    /// Resolves a layout root to a directory, or null when this platform has none.
    /// </summary>
    /// <remarks>
    /// The Windows-only root yields null elsewhere rather than an empty string, which
    /// <c>GetFolderPath</c> returns off-Windows and which would silently resolve layouts against the
    /// process working directory.
    /// </remarks>
    private static string? ResolveRoot(AcpToolchainLayoutRoot root)
    {
        switch (root)
        {
            case AcpToolchainLayoutRoot.UserHome:
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return string.IsNullOrEmpty(home) ? null : home;

            case AcpToolchainLayoutRoot.WindowsRoamingAppData:
                if (!OperatingSystem.IsWindows())
                {
                    return null;
                }

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return string.IsNullOrEmpty(appData) ? null : appData;

            case AcpToolchainLayoutRoot.WindowsProgramFiles:
                if (!OperatingSystem.IsWindows())
                {
                    return null;
                }

                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return string.IsNullOrEmpty(programFiles) ? null : programFiles;

            case AcpToolchainLayoutRoot.SalmonEggToolchains:
                // Resolved here rather than in the domain for the same reason the platform roots are:
                // where app data lives is an infrastructure decision. Kept identical to what the
                // toolchain installer writes, so an install is discovered by this scan.
                return Path.Combine(SalmonEggPaths.GetAppDataRootPath(), ToolchainsDirectoryName);

            case AcpToolchainLayoutRoot.Absolute:
                return string.Empty;

            default:
                return null;
        }
    }

    private static IEnumerable<string> ExpandFixed(string root, IReadOnlyList<string> segments)
    {
        if (Combine(root, segments, 0, segments.Count) is { } directory && Directory.Exists(directory))
        {
            yield return directory;
        }
    }

    /// <summary>
    /// Expands the one wildcard segment against the version directories present.
    /// </summary>
    /// <remarks>
    /// Enumerated with <c>EnumerateDirectories</c> on the parent rather than a recursive glob: the depth is
    /// known from the layout, so there is no reason to walk a user's home directory.
    /// </remarks>
    private static IEnumerable<string> ExpandVersioned(string root, IReadOnlyList<string> segments)
    {
        var wildcardIndex = IndexOfWildcard(segments);
        if (Combine(root, segments, 0, wildcardIndex) is not { } parent || !Directory.Exists(parent))
        {
            yield break;
        }

        string[] versionDirectories;
        try
        {
            versionDirectories = Directory.GetDirectories(parent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A directory the user cannot read contributes nothing, like one that is not there.
            yield break;
        }

        // Newest first by name. Not semantic version ordering: these are directory names a manager chose,
        // and the wizard shows candidates for the user to pick rather than deciding for them.
        Array.Sort(versionDirectories, static (left, right) => string.CompareOrdinal(right, left));

        foreach (var versionDirectory in versionDirectories)
        {
            if (Combine(versionDirectory, segments, wildcardIndex + 1, segments.Count) is { } directory
                && Directory.Exists(directory))
            {
                yield return directory;
            }
        }
    }

    private static int IndexOfWildcard(IReadOnlyList<string> segments)
    {
        for (var index = 0; index < segments.Count; index++)
        {
            if (string.Equals(segments[index], AcpToolchainLayout.VersionWildcard, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Joins <paramref name="segments"/> in <c>[start, end)</c> onto <paramref name="root"/>, or null when
    /// the result is not a path this platform can express.
    /// </summary>
    private static string? Combine(string root, IReadOnlyList<string> segments, int start, int end)
    {
        try
        {
            var path = root;
            for (var index = start; index < end; index++)
            {
                path = Path.Combine(path, segments[index]);
            }

            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
