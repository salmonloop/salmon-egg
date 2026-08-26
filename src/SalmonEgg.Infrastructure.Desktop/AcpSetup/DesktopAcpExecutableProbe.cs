using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Desktop implementation of <see cref="IAcpExecutableProbe"/>: resolves executables against the real
/// PATH and asks the Node/uv launchers what they have installed.
/// </summary>
/// <remarks>
/// Every query answers with a tri-state where the caller can act on "unknown": a launcher that fails to
/// run yields null rather than false, so the wizard reports an undetermined probe instead of telling the
/// user to install something that may already be there.
/// </remarks>
public sealed class DesktopAcpExecutableProbe : IAcpExecutableProbe
{
    private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PackageQueryTimeout = TimeSpan.FromSeconds(60);

    public bool SupportsProcessProbing => true;

    public Task<string?> ResolveExecutablePathAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ResolveExecutablePath(command));
    }

    public Task<IReadOnlyList<string>> ResolveExecutableCandidatesAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ResolveExecutableCandidates(command));
    }

    public async Task<string?> ReadVersionAsync(
        string command,
        IReadOnlyList<string> versionArguments,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command) || versionArguments is null)
        {
            return null;
        }

        var executable = ResolveExecutablePath(command);
        if (executable is null)
        {
            return null;
        }

        var result = await AcpSetupProcessRunner
            .RunAsync(executable, versionArguments, VersionProbeTimeout, onOutputLine: null, cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded ? FirstNonEmptyLine(result.CombinedOutput) : null;
    }

    /// <summary>
    /// Asks the caller-chosen package manager for its global list.
    /// </summary>
    /// <remarks>
    /// For npm, <c>--depth 0</c> keeps the walk to top-level packages and <c>--parseable</c> yields one
    /// path per line, which is stable across npm versions.
    ///
    /// Candidates are tried in order and the first one that resolves is asked. Only resolution is retried,
    /// never a manager's answer: a manager that ran and said "no" has answered for the toolchain the
    /// caller chose, and trying the next candidate would replace that answer with one about a different
    /// toolchain.
    /// </remarks>
    public async Task<AcpPackageQueryResult> LocateGlobalPackageAsync(
        AcpDistributionKind distribution,
        string packageId,
        AcpPackageManagerCandidates packageManager,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageManager);

        if (string.IsNullOrWhiteSpace(packageId) || ResolveListArguments(distribution) is not { } arguments)
        {
            return AcpPackageQueryResult.Unknown();
        }

        foreach (var candidate in packageManager.Commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ResolveExecutablePath(candidate) is { } executable)
            {
                return await QueryPackageListAsync(executable, arguments, packageId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return AcpPackageQueryResult.Unknown();
    }

    /// <summary>
    /// The list-installed invocation for <paramref name="distribution"/>, or null when it has no package
    /// manager to ask.
    /// </summary>
    private static IReadOnlyList<string>? ResolveListArguments(AcpDistributionKind distribution)
        => distribution switch
        {
            AcpDistributionKind.Npx => new[] { "ls", "--global", "--depth", "0", "--parseable" },
            AcpDistributionKind.Uvx => new[] { "tool", "list" },
            _ => null
        };

    /// <summary>
    /// Runs a package-list command and looks for the package name in its output.
    /// </summary>
    /// <remarks>
    /// Returns null — not false — when the command fails to run at all, because an unanswerable query
    /// must not be reported as a definitive absence.
    ///
    /// npm exits non-zero for unrelated reasons (peer-dependency complaints in the global root, for
    /// instance) while still printing a usable list, so output that contains the package is trusted even
    /// on a non-zero exit. The reverse is not true: a failed run with no match stays unknown.
    /// </remarks>
    private static async Task<AcpPackageQueryResult> QueryPackageListAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string packageId,
        CancellationToken cancellationToken)
    {
        var result = await AcpSetupProcessRunner
            .RunAsync(executable, arguments, PackageQueryTimeout, onOutputLine: null, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Started)
        {
            return AcpPackageQueryResult.Unknown();
        }

        var packageName = StripVersionSuffix(packageId);
        if (FindPackageLocation(result.CombinedOutput, packageName) is { } location)
        {
            return AcpPackageQueryResult.Found(location, executable);
        }

        return result.Succeeded
            ? AcpPackageQueryResult.Absent(executable)
            : AcpPackageQueryResult.Unknown(executable);
    }

    /// <summary>
    /// Returns the line reporting <paramref name="packageName"/>, or null when no line reports it.
    /// </summary>
    /// <remarks>
    /// The package name is matched as a whole trailing path segment, not as a substring of the output.
    /// A substring test reports a package as installed whenever some unrelated package merely contains
    /// its name — searching for <c>cline</c> matches <c>my-cline-fork</c> — and a false "installed" is the
    /// worst of the three answers here: the wizard skips the install, advances, and fails at launch.
    ///
    /// <c>npm ls --parseable</c> prints one filesystem path per line ending in the package directory, and
    /// <c>uv tool list</c> prints the tool name first on the line, so comparing the last path segment
    /// covers both. Scoped names keep their <c>@scope/</c> prefix, which is itself a path separator on
    /// disk, so the comparison is made against the last two segments when the name is scoped.
    /// </remarks>
    internal static string? FindPackageLocation(string output, string packageName)
    {
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(packageName))
        {
            return null;
        }

        var segmentCount = packageName.StartsWith('@') ? 2 : 1;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // uv reports "name version"; npm reports a path. Take the first token either way.
            var token = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]
                .TrimEnd('/', '\\');
            if (TrailingSegments(token, segmentCount) is { } tail
                && string.Equals(tail, packageName, StringComparison.OrdinalIgnoreCase))
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>Returns the last <paramref name="count"/> path segments, or null when there are fewer.</summary>
    private static string? TrailingSegments(string path, int count)
    {
        var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < count)
        {
            return null;
        }

        return string.Join('/', segments[^count..]);
    }

    /// <summary>
    /// Drops a pinned version from a package coordinate, preserving the leading '@' of a scoped name.
    /// </summary>
    internal static string StripVersionSuffix(string packageId)
    {
        var trimmed = packageId.Trim();
        var separator = trimmed.LastIndexOf('@');
        return separator > 0 ? trimmed[..separator] : trimmed;
    }

    private static string? FirstNonEmptyLine(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves <paramref name="command"/> the way a shell would: an explicit path is used as given,
    /// otherwise PATH is searched, applying PATHEXT on Windows so <c>npm</c> finds <c>npm.cmd</c>.
    /// </summary>
    private static string? ResolveExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        if (trimmed.IndexOfAny(new[] { '/', '\\' }) >= 0)
        {
            return File.Exists(trimmed) ? Path.GetFullPath(trimmed) : null;
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var pathSeparator = isWindows ? ';' : ':';
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var extension in ResolveExtensions(isWindows))
        {
            var candidateName = trimmed + extension;
            foreach (var directory in directories)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, candidateName);
                }
                catch (ArgumentException)
                {
                    // PATH entries can contain characters that are invalid for this platform's paths.
                    continue;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Enumerates every distinct executable <paramref name="command"/> matches, in PATH order.
    /// </summary>
    /// <remarks>
    /// Deduplicated by resolved target rather than by candidate string: PATH commonly lists the same
    /// directory more than once, and per-user bin directories are often symlink farms pointing at one
    /// real file. Without that, a machine with one install reports several identical candidates and the
    /// UI offers a choice between a path and itself.
    ///
    /// An explicit path is returned as its single candidate, matching the single-path overload — there is
    /// no PATH search to enumerate.
    /// </remarks>
    private static IReadOnlyList<string> ResolveExecutableCandidates(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Array.Empty<string>();
        }

        var trimmed = command.Trim();
        if (trimmed.IndexOfAny(new[] { '/', '\\' }) >= 0)
        {
            var explicitPath = ResolveExecutablePath(trimmed);
            return explicitPath is null ? Array.Empty<string>() : new[] { explicitPath };
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var pathSeparator = isWindows ? ';' : ':';
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var candidates = new List<string>();
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);

        foreach (var extension in ResolveExtensions(isWindows))
        {
            var candidateName = trimmed + extension;
            foreach (var directory in directories)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, candidateName);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!File.Exists(candidate))
                {
                    continue;
                }

                if (seenTargets.Add(ResolveDeduplicationKey(candidate)))
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates;
    }

    /// <summary>
    /// The identity two candidate paths are compared on: the symlink target when one can be read, the
    /// full path otherwise.
    /// </summary>
    private static string ResolveDeduplicationKey(string candidate)
    {
        try
        {
            var info = new FileInfo(candidate);
            return info.LinkTarget is null
                ? info.FullName
                : (info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable candidate still counts as itself rather than collapsing into another entry.
            return candidate;
        }
    }

    private static IEnumerable<string> ResolveExtensions(bool isWindows)
    {
        if (!isWindows)
        {
            yield return string.Empty;
            yield break;
        }

        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        foreach (var extension in (string.IsNullOrWhiteSpace(pathExt) ? ".COM;.EXE;.BAT;.CMD" : pathExt)
                 .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return extension.StartsWith('.') ? extension : "." + extension;
        }

        yield return string.Empty;
    }
}
