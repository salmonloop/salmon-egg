using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Adds an installed toolchain's directory to the user's persistent PATH, so their own terminal can use
/// what the wizard installed.
/// </summary>
/// <remarks>
/// Strictly per-user, on every platform. A system-wide location would need elevation, which turns a
/// declined prompt into a broken feature, cannot be undone by uninstalling the app, and changes the
/// environment of users who never asked for it. Per-user needs no privileges and is reversible.
///
/// This is not what makes the wizard work — the wizard finds the install through its own directory scan.
/// It exists so a user who installed Node here is not left with a Node their shell cannot see, which would
/// be a confusing half-install. That is why failure is reported rather than thrown: the toolchain is
/// installed and usable either way.
/// </remarks>
internal static class UserPathRegistration
{
    /// <summary>Opening marker of the block written to a POSIX profile.</summary>
    internal const string BlockStart = "# >>> SalmonEgg toolchains >>>";

    /// <summary>Closing marker of the block written to a POSIX profile.</summary>
    internal const string BlockEnd = "# <<< SalmonEgg toolchains <<<";

    /// <summary>
    /// Ensures <paramref name="directory"/> is on the user's persistent PATH.
    /// </summary>
    /// <remarks>
    /// Idempotent by design rather than as an optimization. The wizard can install repeatedly — a retry, a
    /// second agent needing the same toolchain, a version upgrade — and an entry appended each time grows
    /// PATH without bound. On Windows that eventually exceeds the environment block limit and breaks
    /// process creation for the whole user session, which is a far worse outcome than the install failing.
    /// </remarks>
    internal static AcpPathRegistration Register(string directory, Action<string>? onOutput = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return AcpPathRegistration.Failed;
        }

        try
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? RegisterOnWindows(directory, onOutput)
                : RegisterOnPosix(directory, onOutput);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Reported, never thrown: the toolchain is installed and the wizard can already use it. A
            // read-only profile or a locked registry hive must not undo a successful install.
            onOutput?.Invoke($"Could not update PATH: {exception.Message}");
            return AcpPathRegistration.Failed;
        }
    }

    /// <summary>
    /// Appends to the user's own PATH value in the registry and tells running processes it changed.
    /// </summary>
    /// <remarks>
    /// The existing value is read back from the <em>user</em> target, never from this process's PATH. The
    /// process value is the user and machine values already merged, so appending to it and storing the
    /// result would copy the entire system PATH into the user's own — permanently, invisibly, and in a way
    /// that then shadows later system changes. This is the single most damaging mistake available here.
    /// </remarks>
    private static AcpPathRegistration RegisterOnWindows(string directory, Action<string>? onOutput)
    {
        if (!OperatingSystem.IsWindows())
        {
            return AcpPathRegistration.Failed;
        }

        var existing = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
            ?? string.Empty;
        if (ContainsDirectory(existing, directory))
        {
            return AcpPathRegistration.AlreadyPresent;
        }

        var updated = existing.Length == 0
            ? directory
            : existing.TrimEnd(';') + ";" + directory;
        Environment.SetEnvironmentVariable("PATH", updated, EnvironmentVariableTarget.User);

        // Without this, only processes started after the next sign-out observe the change: the registry
        // write is durable but nothing re-reads it. Explorer rebroadcasts to its children, so a terminal
        // opened afterwards sees the new PATH.
        NotifyEnvironmentChanged();
        onOutput?.Invoke($"Added to your user PATH: {directory}");
        return AcpPathRegistration.Applied;
    }

    /// <summary>
    /// Writes a marked block into <c>~/.profile</c> that prepends the directory to PATH.
    /// </summary>
    /// <remarks>
    /// <c>~/.profile</c> rather than a shell-specific rc file: it is the POSIX login entry point that bash,
    /// zsh (via its own profile chain), sh and dash all honour, so one write serves whatever shell the user
    /// runs. Writing several rc files instead would inject the same entry more than once on a machine with
    /// more than one shell.
    ///
    /// The block is delimited by markers so a repeat install rewrites it in place, and so a user who wants
    /// it gone has an unambiguous region to delete. Guarded with a directory test at shell startup, because
    /// a profile is read on every login and must not add a stale entry after the user deletes the toolchain.
    /// </remarks>
    private static AcpPathRegistration RegisterOnPosix(string directory, Action<string>? onOutput)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return AcpPathRegistration.Failed;
        }

        var profile = Path.Combine(home, ".profile");
        var existingLines = File.Exists(profile)
            ? new List<string>(File.ReadAllLines(profile))
            : new List<string>();

        var block = BuildPosixBlock(directory);
        var start = existingLines.FindIndex(line => line.Trim() == BlockStart);
        var end = existingLines.FindIndex(line => line.Trim() == BlockEnd);

        if (start >= 0 && end > start)
        {
            var current = existingLines.GetRange(start, end - start + 1);
            if (current.Count == block.Count && SequenceEqual(current, block))
            {
                return AcpPathRegistration.AlreadyPresent;
            }

            // Replaced in place rather than appended, so repeated installs leave exactly one block.
            existingLines.RemoveRange(start, end - start + 1);
            existingLines.InsertRange(start, block);
        }
        else
        {
            if (existingLines.Count > 0 && existingLines[^1].Length > 0)
            {
                existingLines.Add(string.Empty);
            }

            existingLines.AddRange(block);
        }

        File.WriteAllText(profile, string.Join('\n', existingLines) + "\n");
        onOutput?.Invoke($"Added to {profile}: {directory}");
        return AcpPathRegistration.Applied;
    }

    private static List<string> BuildPosixBlock(string directory)
    {
        // A user's home path is normally mundane, but it is not constrained by this app. Escaping before
        // inserting it into shell source is non-negotiable: a quote, dollar, backtick, or backslash in a
        // legal directory name must stay a directory name rather than become part of the next login's
        // program. Double quotes preserve spaces while the helper suppresses every expansion they permit.
        var quotedDirectory = QuoteForPosixDoubleQuotes(directory);
        return new List<string>
        {
            BlockStart,
            "# Managed by SalmonEgg. Edit or delete the whole block.",
            $"if [ -d \"{quotedDirectory}\" ]; then",
            $"  case \":$PATH:\" in *\":{quotedDirectory}:\"*) ;; *) PATH=\"{quotedDirectory}:$PATH\" ;; esac",
            "  export PATH",
            "fi",
            BlockEnd
        };
    }

    /// <summary>Escapes a value inserted inside POSIX shell double quotes.</summary>
    private static string QuoteForPosixDoubleQuotes(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);

    private static bool SequenceEqual(List<string> left, List<string> right)
    {
        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].TrimEnd(), right[index].TrimEnd(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="path"/> already lists <paramref name="directory"/>.
    /// </summary>
    /// <remarks>
    /// Trailing separators are ignored and Windows is compared case-insensitively, because a PATH entry
    /// that differs only that way is the same directory and adding it again would be a duplicate.
    /// </remarks>
    private static bool ContainsDirectory(string path, string directory)
    {
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var target = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var entry in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(
                    entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    target,
                    comparison))
            {
                return true;
            }
        }

        return false;
    }

    private const int WmSettingChange = 0x001A;
    private const int SmtoAbortIfHung = 0x0002;

    private static void NotifyEnvironmentChanged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            // Broadcast to top-level windows with a short timeout. A hung shell must not hold the wizard,
            // and a missed broadcast only delays visibility to the next sign-in.
            SendMessageTimeout(
                new IntPtr(0xFFFF),
                WmSettingChange,
                IntPtr.Zero,
                "Environment",
                SmtoAbortIfHung,
                timeout: 1000,
                out _);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Nothing to do: the registry write already happened and is what persists.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        string lParam,
        int flags,
        int timeout,
        out IntPtr result);
}
