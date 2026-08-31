using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SalmonEgg.Infrastructure.Transport;

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
    {
        if (string.IsNullOrWhiteSpace(command)
            || command.IndexOfAny(['/', '\\']) >= 0
            || Path.HasExtension(command)
            || !isWindows)
        {
            return command;
        }

        if (File.Exists(Path.Combine(currentDirectory, command)))
        {
            return command;
        }

        var pathDirectories = (pathEnvironment ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var extension in ResolveWindowsPathExtensions(pathExtensions))
        {
            var candidate = string.IsNullOrEmpty(extension) ? command : command + extension;
            if (File.Exists(Path.Combine(currentDirectory, candidate)))
            {
                return candidate;
            }

            foreach (var directory in pathDirectories)
            {
                var candidatePath = Path.Combine(directory, candidate);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }
        }

        return command;
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
