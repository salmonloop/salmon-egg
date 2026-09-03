using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SalmonEgg.Infrastructure.Transport;

internal sealed record StdioCommandResolution(
    string Command,
    bool ResolvedToExistingFile,
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
    /// Resolves a command the same way <see cref="Resolve"/> does, and additionally reports whether
    /// the resolved command names a file that actually exists, plus the directories that were
    /// searched. The preflight and the process start must both consume this one resolution — a
    /// second resolver with different rules is exactly the drift this exists to prevent.
    /// </summary>
    internal static StdioCommandResolution TryResolve(
        string command,
        bool isWindows,
        string currentDirectory,
        string? pathEnvironment,
        string? pathExtensions)
    {
        if (isWindows)
        {
            return TryResolveWindows(command, currentDirectory, pathEnvironment, pathExtensions);
        }

        return TryResolveUnix(command, currentDirectory, pathEnvironment);
    }

    private static StdioCommandResolution TryResolveWindows(
        string command,
        string currentDirectory,
        string? pathEnvironment,
        string? pathExtensions)
    {
        // An explicit path, an empty command, or a name that already carries an extension is
        // returned as-is by Resolve; the first two are never launchable as given, and the last one
        // CreateProcess looks up by its literal name (PATHEXT is not applied), so the literal name
        // is what existence has to be judged against.
        if (string.IsNullOrWhiteSpace(command)
            || command.IndexOfAny(['/', '\\']) >= 0)
        {
            return new StdioCommandResolution(command, File.Exists(Path.Combine(currentDirectory, command)), Array.Empty<string>());
        }

        if (Path.HasExtension(command))
        {
            var literalSearch = SearchDirectories(currentDirectory, pathEnvironment, ';');
            return new StdioCommandResolution(
                command,
                File.Exists(Path.Combine(currentDirectory, command)) || FindOnPath(command, literalSearch) is not null,
                literalSearch);
        }

        var pathDirectories = (pathEnvironment ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // The current directory is searched first (mirroring CreateProcess), so it leads the trail.
        var searched = SearchDirectories(currentDirectory, pathEnvironment, ';');

        foreach (var extension in ResolveWindowsPathExtensions(pathExtensions))
        {
            var candidate = string.IsNullOrEmpty(extension) ? command : command + extension;
            if (File.Exists(Path.Combine(currentDirectory, candidate)))
            {
                return Found(candidate, searched);
            }

            foreach (var directory in pathDirectories)
            {
                var candidatePath = Path.Combine(directory, candidate);
                if (File.Exists(candidatePath))
                {
                    return Found(candidatePath, searched);
                }
            }
        }

        return new StdioCommandResolution(command, false, searched);
    }

    private static StdioCommandResolution TryResolveUnix(
        string command,
        string currentDirectory,
        string? pathEnvironment)
    {
        // Command is returned unchanged (the OS execvp does the PATH search at start time), but
        // existence is still reported so the preflight can fail fast with an actionable message.
        // A relative path is judged against the parent process's current directory: on Unix,
        // Process.Start execs the file before changing to ProcessStartInfo.WorkingDirectory.
        if (string.IsNullOrWhiteSpace(command))
        {
            return new StdioCommandResolution(command, false, Array.Empty<string>());
        }

        if (command.Contains('/'))
        {
            return new StdioCommandResolution(command, File.Exists(Path.Combine(currentDirectory, command)), Array.Empty<string>());
        }

        var searched = SearchDirectories(currentDirectory, pathEnvironment, ':');
        return new StdioCommandResolution(
            command,
            File.Exists(Path.Combine(currentDirectory, command)) || FindOnPath(command, searched) is not null,
            searched);
    }

    private static StdioCommandResolution Found(string resolvedCommand, IReadOnlyList<string> searchedDirectories)
        => new(resolvedCommand, true, searchedDirectories);

    private static IReadOnlyList<string> SearchDirectories(
        string currentDirectory,
        string? pathEnvironment,
        char separator)
    {
        var directories = new List<string> { currentDirectory };
        directories.AddRange(
            (pathEnvironment ?? string.Empty)
            .Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return directories;
    }

    private static string? FindOnPath(string command, IReadOnlyList<string> searchedDirectories)
    {
        foreach (var directory in searchedDirectories)
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
