using System;
using System.IO;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Builds the shell-ready command that makes an executable report the environment its shell handed it.
/// </summary>
/// <remarks>
/// The capture works by having the user's shell start a program that prints its own environment. Which
/// program that is belongs to the host rather than to this type: the CLI implements the printing mode, and
/// it ships as its own package installed independently of the desktop app, so whether one is present is a
/// deployment fact the caller establishes.
///
/// The result is one string because the shell is invoked as <c>-c &lt;command&gt;</c> and parses it itself.
/// Paths routinely contain spaces — "Program Files", "Application Support" — so each is quoted; an
/// unquoted path would be read as several arguments.
/// </remarks>
internal static class PrintEnvironmentCommandFactory
{
    /// <summary>The option that selects environment printing. Must match the CLI's own spelling.</summary>
    /// <remarks>
    /// Held as a constant rather than referenced from the CLI assembly, because the two are separate
    /// executables that meet only across a process boundary and neither references the other. A test pins
    /// the two spellings together so they cannot drift apart silently.
    /// </remarks>
    internal const string OptionName = "--printenv";

    /// <summary>
    /// Returns a factory turning a marker into the command that runs
    /// <paramref name="printEnvironmentExecutable"/>, or null when that executable is unusable.
    /// </summary>
    /// <remarks>
    /// Null rather than a best guess: a command built around a path that is not there produces a shell
    /// failure indistinguishable from a user's broken rc file, so the caller should skip the capture and
    /// keep the inherited PATH instead of reporting one that never had a chance.
    /// </remarks>
    internal static Func<string, string>? TryCreate(string? printEnvironmentExecutable)
    {
        if (string.IsNullOrWhiteSpace(printEnvironmentExecutable)
            || !File.Exists(printEnvironmentExecutable))
        {
            return null;
        }

        var invocation = Quote(printEnvironmentExecutable);
        return marker => invocation + " " + Quote(OptionName + "=" + marker);
    }

    /// <summary>
    /// Wraps <paramref name="value"/> in single quotes for a POSIX shell.
    /// </summary>
    /// <remarks>
    /// Single quotes because they suppress every expansion a shell would otherwise apply to a path: no
    /// variable substitution, no globbing, no backslash escapes. An embedded single quote is closed,
    /// escaped, and reopened — the only sequence POSIX defines for it.
    ///
    /// POSIX only, because only POSIX shells are captured: Windows has no profile-built PATH to recover, so
    /// no command is ever built for it.
    /// </remarks>
    private static string Quote(string value)
        => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
