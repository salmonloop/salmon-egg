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

        // PATH HANDLING: If the command already specifies a path, don't attempt to resolve it.
        // Launchers such as ssh are also valid stdio commands and should flow through unchanged.
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
        // DEBUGGING: Command resolution is a common failure point for background agents.
        // We log the current context to help developers trace why a command might be "not found".
        System.Diagnostics.Debug.WriteLine($"[PathResolver] Resolving command: {command}");
        System.Diagnostics.Debug.WriteLine($"[PathResolver] Current directory: {Environment.CurrentDirectory}");

        string currentPath = Path.Combine(Environment.CurrentDirectory, command);
        if (File.Exists(currentPath))
        {
            System.Diagnostics.Debug.WriteLine($"[PathResolver] Found in current directory: {currentPath}");
            return command;
        }

        // HEURISTICS: ACP Client might send 'npm' instead of 'npm.cmd'.
        // We try common executable extensions to ensure compatibility with various shell aliases.
        foreach (var ext in new[] { ".exe", ".bat", ".cmd", ".ps1", "" })
        {
            string candidate = string.IsNullOrEmpty(ext) ? command : command + ext;

            string fullPath = Path.Combine(Environment.CurrentDirectory, candidate);
            if (File.Exists(fullPath))
            {
                System.Diagnostics.Debug.WriteLine($"[PathResolver] Found in current directory (with extension): {fullPath}");
                return candidate;
            }

            // SYSTEM SEARCH: Fallback to PATH environment variable to emulate shell behavior.
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            foreach (var pathDir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(pathDir)) continue;

                string pathFile = Path.Combine(pathDir.Trim(), candidate);
                if (File.Exists(pathFile))
                {
                    System.Diagnostics.Debug.WriteLine($"[PathResolver] Found in PATH: {pathFile}");
                    return pathFile;
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[PathResolver] File not found, returning original command: {command}");
        return command;
    }
}
