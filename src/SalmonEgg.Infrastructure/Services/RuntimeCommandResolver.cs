using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SalmonEgg.Infrastructure.Services;

public static class RuntimeCommandResolver
{
    public static bool TryResolve(string commandName, out string commandPath)
    {
        commandPath = string.Empty;
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return false;
        }

        if (Path.IsPathRooted(commandName)
            || commandName.IndexOf(Path.DirectorySeparatorChar) >= 0
            || commandName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
        {
            if (File.Exists(commandName))
            {
                commandPath = commandName;
                return true;
            }

            return false;
        }

        foreach (var candidate in EnumerateCandidateNames(commandName))
        {
            foreach (var directory in EnumeratePathDirectories())
            {
                var fullPath = Path.Combine(directory, candidate);
                if (File.Exists(fullPath))
                {
                    commandPath = fullPath;
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateCandidateNames(string commandName)
    {
        yield return commandName;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Path.HasExtension(commandName))
        {
            yield break;
        }

        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        var extensions = string.IsNullOrWhiteSpace(pathExt)
            ? [".exe", ".cmd", ".bat", ".ps1"]
            : pathExt.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                continue;
            }

            yield return commandName + extension.Trim();
        }
    }

    private static IEnumerable<string> EnumeratePathDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            yield return directory.Trim();
        }
    }
}
