using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
    /// Asks npm for the global package list. <c>--depth 0</c> keeps the walk to top-level packages, and
    /// <c>--parseable</c> yields one path per line, which is stable across npm versions.
    /// </summary>
    public Task<bool?> IsGlobalNodePackageInstalledAsync(
        string packageId,
        CancellationToken cancellationToken = default)
        => QueryPackageListAsync(
            launcher: "npm",
            arguments: new[] { "ls", "--global", "--depth", "0", "--parseable" },
            packageId,
            cancellationToken);

    public Task<bool?> IsGlobalUvToolInstalledAsync(
        string packageId,
        CancellationToken cancellationToken = default)
        => QueryPackageListAsync(
            launcher: "uv",
            arguments: new[] { "tool", "list" },
            packageId,
            cancellationToken);

    /// <summary>
    /// Runs a package-list command and looks for the package name in its output.
    /// </summary>
    /// <remarks>
    /// Returns null — not false — when the launcher is missing or the command fails, because an
    /// unanswerable query must not be reported as a definitive absence.
    ///
    /// npm exits non-zero for unrelated reasons (peer-dependency complaints in the global root, for
    /// instance) while still printing a usable list, so output that contains the package is trusted even
    /// on a non-zero exit. The reverse is not true: a failed run with no match stays unknown.
    /// </remarks>
    private static async Task<bool?> QueryPackageListAsync(
        string launcher,
        IReadOnlyList<string> arguments,
        string packageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        var executable = ResolveExecutablePath(launcher);
        if (executable is null)
        {
            return null;
        }

        var result = await AcpSetupProcessRunner
            .RunAsync(executable, arguments, PackageQueryTimeout, onOutputLine: null, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Started)
        {
            return null;
        }

        var packageName = StripVersionSuffix(packageId);
        if (ContainsPackage(result.CombinedOutput, packageName))
        {
            return true;
        }

        return result.Succeeded ? false : null;
    }

    private static bool ContainsPackage(string output, string packageName)
        => !string.IsNullOrWhiteSpace(output)
            && !string.IsNullOrWhiteSpace(packageName)
            && output.Contains(packageName, StringComparison.OrdinalIgnoreCase);

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
