using System;
using System.Text;

namespace SalmonEgg.Infrastructure.Desktop.Services;

/// <summary>
/// Builds the shell commands the macOS authorization dialog runs, with both layers of quoting.
/// </summary>
/// <remarks>
/// Two nested languages, each with its own escaping, and getting either wrong is a command-injection bug
/// rather than a formatting one: the text is handed to <c>osascript -e 'do shell script "…" with
/// administrator privileges'</c>, so it is first parsed as an AppleScript string literal and then executed
/// by <c>/bin/sh</c>. Paths reaching here come from the running bundle's own location, not from user input,
/// but a bundle can sit under a directory containing a quote or a backslash — an app copied to
/// <c>/Users/o'brien/Applications</c> is enough — and the failure mode is running whatever that path
/// happens to spell as root.
///
/// Kept apart from the service that executes it so the escaping is testable without an authorization
/// prompt, the same reason the MSI PATH rule lives apart from the COM code that reads a package.
/// </remarks>
internal static class MacOsPrivilegedShellScript
{
    /// <summary>
    /// The shell command that points the PATH entry at this app's bundled command.
    /// </summary>
    /// <remarks>
    /// Remove-then-link rather than <c>ln -sf</c>: when the existing path is a symlink to a directory, -f
    /// makes ln create the new link inside it. Same reasoning as the pkg's postinstall, and the two are
    /// deliberately the same shape so an install and an in-app link produce an identical result.
    /// </remarks>
    internal static string BuildLinkCommand(string source, string destination, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        return $"/bin/mkdir -p {QuoteForShell(destinationDirectory)} && " +
               $"/bin/rm -f {QuoteForShell(destination)} && " +
               $"/bin/ln -s {QuoteForShell(source)} {QuoteForShell(destination)}";
    }

    internal static string BuildUnlinkCommand(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        return $"/bin/rm -f {QuoteForShell(destination)}";
    }

    /// <summary>
    /// Wraps a shell command as the single <c>-e</c> argument osascript evaluates.
    /// </summary>
    internal static string BuildOsaScriptStatement(string shellCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellCommand);
        return $"do shell script \"{EscapeForAppleScript(shellCommand)}\" with administrator privileges";
    }

    /// <summary>
    /// Single-quotes a value for <c>/bin/sh</c>.
    /// </summary>
    /// <remarks>
    /// Inside single quotes the shell treats every character literally, including backslashes and double
    /// quotes, so the only character needing attention is the single quote itself. It is closed, escaped
    /// outside the quotes, and reopened — the standard <c>'\''</c> form — because there is no escape for it
    /// within a single-quoted string.
    /// </remarks>
    internal static string QuoteForShell(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// Escapes a string for an AppleScript literal.
    /// </summary>
    /// <remarks>
    /// AppleScript literals recognize only two escapes, <c>\\</c> and <c>\"</c>, and the backslash has to be
    /// handled first or escaping the quote would double back over it. Anything else — including the
    /// <c>'\''</c> sequences the shell quoting just produced — passes through as written, which is the point:
    /// the shell must receive exactly what QuoteForShell built.
    /// </remarks>
    internal static string EscapeForAppleScript(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            if (character is '\\' or '"')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
