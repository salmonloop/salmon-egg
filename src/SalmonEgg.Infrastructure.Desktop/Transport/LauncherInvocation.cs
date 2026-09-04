using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SalmonEgg.Infrastructure.Transport;

/// <summary>
/// One launcher invocation normalized for <c>CreateProcess</c>: the executable to start, the arguments to
/// pass it, and the command those were derived from.
/// </summary>
/// <remarks>
/// Two rules are shared by everything in this app that starts an external CLI — the ACP wizard's probes
/// and installs, and the stdio transport that carries a real conversation — so they live here rather than
/// in either caller:
///
/// 1. A <c>.cmd</c> or <c>.bat</c> launcher is not an executable image. <c>CreateProcess</c> requires the
///    command interpreter to be named explicitly ("To run a batch file, you must start the command
///    interpreter; set lpApplicationName to cmd.exe and set lpCommandLine to /c plus the name of the
///    batch file"), and .NET reaches <c>CreateProcess</c> directly whenever
///    <c>UseShellExecute</c> is false — which redirecting stdio requires. Starting a batch file
///    without the wrapper fails with Win32 error 193, ERROR_BAD_EXE_FORMAT. This matters because npm
///    installs every Node CLI on Windows as a <c>.cmd</c> shim (npm shells out to cmd-shim, "since
///    symlinks are not suitable for this purpose there").
///
/// 2. The launcher's own directory goes on the child's PATH. A launcher named by absolute path can still
///    fail to run: every npm-installed CLI starts with <c>#!/usr/bin/env node</c>, so it resolves and
///    then exits 127 with "/usr/bin/env: 'node': No such file or directory" when its sibling interpreter
///    is not on PATH. That is the ordinary case for a GUI-launched app, which inherits the session PATH
///    rather than the one a shell profile builds, so a version-manager toolchain (nvm, fnm, volta, asdf)
///    is invisible to it.
/// </remarks>
internal sealed record LauncherInvocation(
    string FileName,
    IReadOnlyList<string> Arguments,
    string ResolvedCommand)
{
    private static readonly StringComparison DirectoryComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Whether <see cref="ResolvedCommand"/> named a file that actually existed at resolution time.
    /// Judged by the same resolver that produced the command, so the preflight and the process start
    /// can never disagree about what "on PATH" means.
    /// </summary>
    internal bool ResolvedToExistingFile { get; init; }

    /// <summary>
    /// Whether the resolution found the command by searching PATH (as opposed to the command naming
    /// its own location, or being unlaunchable as given). The preflight reads this to pick its
    /// message instead of re-inspecting the string for separators — the same drift the existence
    /// verdict is kept off it for.
    /// </summary>
    internal bool SearchedOnPath { get; init; }

    /// <summary>
    /// The directories the resolver searched before giving up. Diagnostics only — never user-facing.
    /// </summary>
    internal IReadOnlyList<string> SearchedDirectories { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalizes <paramref name="command"/> and <paramref name="arguments"/> into something
    /// <c>CreateProcess</c> accepts, resolving the command against PATH and wrapping a batch launcher in
    /// the command interpreter.
    /// </summary>
    /// <remarks>
    /// The resolution runs against the PATH the child will actually get: <paramref name="environment"/>
    /// when the caller configured one (it replaces the inherited block entirely), otherwise the current
    /// process's. Resolving against anything else would let the preflight clear a command that
    /// <c>ApplyTo</c> will then fail to launch — or the reverse.
    /// </remarks>
    public static LauncherInvocation Create(
        string? command,
        IReadOnlyList<string>? arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var trimmedCommand = (command ?? string.Empty).Trim();
        var trimmedArguments = arguments is null
            ? Array.Empty<string>()
            : arguments.Select(argument => (argument ?? string.Empty).Trim()).ToArray();

        var effectivePath = ResolveEffectivePath(environment);
        var resolution = StdioCommandResolver.TryResolve(
            trimmedCommand,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            Environment.CurrentDirectory,
            effectivePath,
            Environment.GetEnvironmentVariable("PATHEXT"));

        var resolvedCommand = resolution.Command;

        if (IsBatchLauncher(resolvedCommand))
        {
            return new LauncherInvocation(
                FileName: "cmd.exe",
                Arguments: new[] { "/c", resolvedCommand }.Concat(trimmedArguments).ToArray(),
                ResolvedCommand: resolvedCommand)
            {
                ResolvedToExistingFile = resolution.ResolvedToExistingFile,
                SearchedOnPath = resolution.SearchedOnPath,
                SearchedDirectories = resolution.SearchedDirectories,
            };
        }

        return new LauncherInvocation(resolvedCommand, trimmedArguments, resolvedCommand)
        {
            ResolvedToExistingFile = resolution.ResolvedToExistingFile,
            SearchedOnPath = resolution.SearchedOnPath,
            SearchedDirectories = resolution.SearchedDirectories,
        };
    }

    /// <summary>
    /// The PATH the child will inherit from <paramref name="environment"/> — the configured value in
    /// full, since ApplyTo only ever prepends to it. Windows environment dictionaries compare keys
    /// case-insensitively, but this is also built from arbitrary dictionaries, so the lookup stays
    /// insensitive everywhere.
    /// </summary>
    private static string? ResolveEffectivePath(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is not null)
        {
            foreach (var key in environment.Keys)
            {
                if (string.Equals(key, "PATH", StringComparison.OrdinalIgnoreCase))
                {
                    return environment[key];
                }
            }
        }

        return Environment.GetEnvironmentVariable("PATH");
    }

    /// <summary>
    /// Applies the executable, the arguments, and the launcher's PATH entry to
    /// <paramref name="startInfo"/>.
    /// </summary>
    /// <remarks>
    /// PATH is written last so the launcher's directory is prepended to whatever the caller configured,
    /// rather than to the inherited value the caller may have deliberately replaced. Prepending is not a
    /// preference that competes with the caller's environment: without it the launcher cannot reach the
    /// interpreter it was written to run under, so there is nothing for the caller's PATH to matter to.
    /// </remarks>
    public void ApplyTo(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        startInfo.FileName = FileName;
        startInfo.ArgumentList.Clear();
        foreach (var argument in Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (ResolveLauncherDirectory() is { } launcherDirectory)
        {
            // Read through the same dictionary that is written, so a caller-configured PATH is the one
            // extended. On Windows this dictionary compares keys case-insensitively, so the inherited
            // "Path" is found and updated in place rather than shadowed by a second "PATH" entry.
            startInfo.Environment["PATH"] = PrependDirectory(
                launcherDirectory,
                startInfo.Environment.TryGetValue("PATH", out var existing) ? existing : null);
        }
    }

    /// <summary>
    /// The directory whose siblings the launcher needs, or null when the command carries no directory —
    /// a bare name that PATH resolution already answered, so PATH needs no help.
    /// </summary>
    private string? ResolveLauncherDirectory()
    {
        if (string.IsNullOrWhiteSpace(ResolvedCommand))
        {
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(ResolvedCommand));
            return string.IsNullOrEmpty(directory) ? null : directory;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Not a path this platform can express. PATH resolution is the caller's only route anyway.
            return null;
        }
    }

    /// <summary>
    /// Returns <paramref name="path"/> with <paramref name="directory"/> in front, or unchanged when it
    /// already leads or contains it.
    /// </summary>
    /// <remarks>
    /// Already-present entries are left alone because the wizard runs a probe per component: appending
    /// unconditionally would grow PATH on every launch, and an oversized environment block is its own
    /// failure on Windows.
    /// </remarks>
    private static string PrependDirectory(string directory, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return directory;
        }

        var separator = Path.PathSeparator;
        foreach (var entry in path.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), directory, DirectoryComparison))
            {
                return path;
            }
        }

        return directory + separator + path;
    }

    private static bool IsBatchLauncher(string command)
        => command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
}
