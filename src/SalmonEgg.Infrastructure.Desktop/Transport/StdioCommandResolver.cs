using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SalmonEgg.Infrastructure.Transport;

/// <summary>
/// One command resolution: what to launch, whether it names a file that exists, whether PATH was
/// searched to reach that verdict, and where it looked.
/// </summary>
/// <param name="Command">The command to launch, resolved exactly as <see cref="StdioCommandResolver.Resolve(string)"/> reports it.</param>
/// <param name="ResolvedToExistingFile">Whether <paramref name="Command"/> names a file that exists.</param>
/// <param name="SearchedOnPath">
/// Whether the command was a name looked up across directories rather than a location given outright.
/// The resolver already decided this to pick its search strategy, so callers read it here instead of
/// re-inspecting the string for separators.
/// </param>
/// <param name="SearchedDirectories">Where the lookup went. Diagnostics only — never user-facing.</param>
internal sealed record StdioCommandResolution(
    string Command,
    bool ResolvedToExistingFile,
    bool SearchedOnPath,
    IReadOnlyList<string> SearchedDirectories);

internal static class StdioCommandResolver
{
    private const string DefaultWindowsPathExtensions = ".COM;.EXE;.BAT;.CMD";

    public static string Resolve(string command)
        => Resolve(
            command,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            Environment.CurrentDirectory,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATHEXT"));

    internal static string Resolve(
        string command,
        bool isWindows,
        string currentDirectory,
        string? pathEnvironment,
        string? pathExtensions)
        => TryResolve(command, isWindows, currentDirectory, pathEnvironment, pathExtensions).Command;

    /// <summary>
    /// Resolves a command the same way <see cref="Resolve(string)"/> does, and additionally reports
    /// whether the resolved command names a file that actually exists. The preflight and the process
    /// start must both consume this one resolution — a second resolver applying its own rules is
    /// exactly the drift this exists to prevent.
    /// </summary>
    internal static StdioCommandResolution TryResolve(
        string command,
        bool isWindows,
        string currentDirectory,
        string? pathEnvironment,
        string? pathExtensions)
        => isWindows
            ? TryResolveWindows(command, currentDirectory, pathEnvironment, pathExtensions)
            : TryResolveUnix(command, currentDirectory, pathEnvironment);

    private static StdioCommandResolution TryResolveWindows(
        string command,
        string currentDirectory,
        string? pathEnvironment,
        string? pathExtensions)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Unlaunchable(command);
        }

        if (command.IndexOfAny(['/', '\\']) >= 0)
        {
            return ExplicitLocation(command, currentDirectory);
        }

        var pathDirectories = SplitDirectories(pathEnvironment, ';');
        var searched = WithCurrentDirectoryFirst(currentDirectory, pathDirectories);

        // CreateProcess does not apply PATHEXT to a name that already carries an extension, so the
        // literal name is the only candidate that could launch — and the command stays unchanged,
        // as it always has.
        if (Path.HasExtension(command))
        {
            return new StdioCommandResolution(command, Exists(command, searched), SearchedOnPath: true, searched);
        }

        foreach (var extension in ResolveWindowsPathExtensions(pathExtensions))
        {
            var candidate = string.IsNullOrEmpty(extension) ? command : command + extension;

            // The current directory is searched first, and a hit there keeps the relative name: the
            // child inherits that directory, so the name still resolves, and rewriting it to an
            // absolute path would change long-standing behaviour for no gain.
            if (File.Exists(Path.Combine(currentDirectory, candidate)))
            {
                return new StdioCommandResolution(candidate, true, SearchedOnPath: true, searched);
            }

            if (Locate(candidate, pathDirectories) is { } located)
            {
                return new StdioCommandResolution(located, true, SearchedOnPath: true, searched);
            }
        }

        return new StdioCommandResolution(command, false, SearchedOnPath: true, searched);
    }

    private static StdioCommandResolution TryResolveUnix(
        string command,
        string currentDirectory,
        string? pathEnvironment)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Unlaunchable(command);
        }

        if (command.Contains('/'))
        {
            return ExplicitLocation(command, currentDirectory);
        }

        // The command is handed to the OS unchanged — execvp does the PATH search at start time — so
        // only the existence verdict is added here, over the directories execvp would consult.
        var searched = WithCurrentDirectoryFirst(currentDirectory, SplitDirectories(pathEnvironment, ':'));
        return new StdioCommandResolution(command, Exists(command, searched), SearchedOnPath: true, searched);
    }

    /// <summary>A command that names its own location: that one place decides, so nothing is searched.</summary>
    /// <remarks>
    /// A relative location is judged against the parent process's current directory rather than the
    /// configured working directory, because that is where both platforms look: Windows resolves it
    /// before applying the start info, and on Unix the file is exec'd before the chdir.
    /// </remarks>
    private static StdioCommandResolution ExplicitLocation(string command, string currentDirectory)
        => new(command, File.Exists(Path.Combine(currentDirectory, command)), SearchedOnPath: false, []);

    /// <summary>A command that could never launch as given, so no lookup is attempted.</summary>
    private static StdioCommandResolution Unlaunchable(string command)
        => new(command, false, SearchedOnPath: false, []);

    private static string[] SplitDirectories(string? pathEnvironment, char separator)
        => (pathEnvironment ?? string.Empty)
            .Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> WithCurrentDirectoryFirst(
        string currentDirectory,
        IReadOnlyList<string> pathDirectories)
    {
        var searched = new List<string>(pathDirectories.Count + 1) { currentDirectory };
        searched.AddRange(pathDirectories);
        return searched;
    }

    private static bool Exists(string command, IReadOnlyList<string> directories)
        => Locate(command, directories) is not null;

    private static string? Locate(string command, IReadOnlyList<string> directories)
    {
        foreach (var directory in directories)
        {
            var candidatePath = Path.Combine(directory, command);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private static IEnumerable<string> ResolveWindowsPathExtensions(string? pathExtensions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in (string.IsNullOrWhiteSpace(pathExtensions)
                     ? DefaultWindowsPathExtensions
                     : pathExtensions)
                 .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = extension.StartsWith(".", StringComparison.Ordinal)
                ? extension
                : "." + extension;
            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }

        yield return string.Empty;
    }
}
