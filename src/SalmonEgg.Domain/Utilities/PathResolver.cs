using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SalmonEgg.Domain.Utilities;

public static class PathResolver
{
    public static string ResolveCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return command;

        // If the command already specifies a path, pass it through unchanged.
        // Launchers such as ssh are also valid stdio commands.
        if (command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
            return command;

        if (Path.HasExtension(command))
            return command;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ResolveWindowsCommand(command);

        return command;
    }

    private static string ResolveWindowsCommand(string command)
    {
        string currentPath = Path.Combine(Environment.CurrentDirectory, command);
        if (File.Exists(currentPath))
            return command;

        // ACP clients may send bare names like "npm" instead of "npm.cmd".
        // Try common executable extensions and fall back to PATH search.
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator);

        foreach (var ext in new[] { ".exe", ".bat", ".cmd", ".ps1", "" })
        {
            string candidate = string.IsNullOrEmpty(ext) ? command : command + ext;

            if (File.Exists(Path.Combine(Environment.CurrentDirectory, candidate)))
                return candidate;

            foreach (var dir in pathDirs)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                if (File.Exists(Path.Combine(dir.Trim(), candidate)))
                    return Path.Combine(dir.Trim(), candidate);
            }
        }

        return command;
    }
}
