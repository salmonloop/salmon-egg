using System;
using System.IO;

namespace SalmonEgg.Infrastructure.Transport;

/// <summary>
/// The preflight that runs before <c>Process.Start</c>: it answers, from the single resolution the
/// launcher invocation already carries, whether the configured command could even be started — and if
/// not, says so in words the user can act on instead of forwarding the raw Win32 error ("系统找不到
/// 指定的文件") that <c>CreateProcess</c> would produce.
/// </summary>
internal static class StdioCommandPreflight
{
    /// <summary>
    /// Returns an actionable error message when the invocation's resolved command does not name an
    /// existing file, or null when the command is startable as resolved. Pure — the existence verdict
    /// was made by <see cref="StdioCommandResolver"/> at resolution time; nothing is probed here.
    /// </summary>
    /// <remarks>
    /// The verdict is read off <see cref="LauncherInvocation.ResolvedCommand"/>, not
    /// <see cref="LauncherInvocation.FileName"/>: a batch launcher is wrapped in <c>cmd.exe</c>, which
    /// always exists, so the underlying command is the only thing worth judging.
    /// </remarks>
    public static string? BuildMissingCommandError(LauncherInvocation invocation)
    {
        if (invocation.ResolvedToExistingFile)
        {
            return null;
        }

        return invocation.SearchedOnPath
            ? $"Agent command '{invocation.ResolvedCommand}' was not found on PATH. Install the agent or configure the full path to its executable."
            : $"The configured agent command '{invocation.ResolvedCommand}' does not exist. Check the path in the agent configuration.";
    }
}
