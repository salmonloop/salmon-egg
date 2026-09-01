using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Prints this process's environment as one JSON object, wrapped in a caller-supplied marker.
/// </summary>
/// <remarks>
/// This exists to be run <em>by the user's own shell</em>. A GUI-launched desktop process inherits the
/// session environment rather than the one a shell profile builds, so a version-manager toolchain (nvm,
/// fnm, volta, asdf, mise) is invisible to it. The app recovers the real environment by asking the user's
/// login shell to run this, then reading what the shell handed the child.
///
/// Every editor that solves this does the same thing — run its own executable inside the shell rather
/// than parse the shell's <c>env</c> output — because rc files print banners, colour codes, and warnings
/// onto the same stream. VS Code runs <c>code -p</c>, Zed runs <c>zed --printenv</c>, JetBrains runs a
/// bundled <c>printenv</c> helper. Emitting JSON from inside the child makes the payload
/// self-delimiting and immune to whatever text surrounds it.
///
/// The marker handles the surrounding noise. The caller generates a fresh random one per invocation and
/// extracts what lies between the two occurrences, so a chatty rc file cannot be mistaken for payload.
/// It must be random rather than fixed: a fixed marker could appear in a variable's value — this process
/// prints its own environment, which includes anything the shell exported — and turn an honest value into
/// a false delimiter.
///
/// It lives here, beside <see cref="ShellEnvironmentCapture"/>, rather than in either executable that
/// implements the mode. Both the CLI and the desktop app answer this protocol, and the capture reads it;
/// putting the writer next to the reader is what keeps the two spellings from drifting. Previously the
/// option name existed twice — once in the CLI, once in the capture's command factory — held together by
/// a test that compared the two constants, which is a check that the drift has not happened yet rather
/// than a structure in which it cannot.
///
/// Whichever executable hosts this must handle it <em>before</em> building a service container or running
/// startup recovery. It reads nothing and writes nothing but stdout, and it is invoked while the app is
/// starting; touching user data here would make an environment probe a source of configuration side
/// effects.
/// </remarks>
public static class DesktopPrintEnvironment
{
    /// <summary>The option that selects this mode. Undocumented: it is an app-internal protocol.</summary>
    public const string OptionName = "--printenv";

    /// <summary>
    /// True when <paramref name="args"/> requests environment printing, yielding the marker to wrap the
    /// payload in.
    /// </summary>
    /// <remarks>
    /// Matched against raw arguments rather than through a command-line parser because no container may be
    /// built for this invocation, and a parser needs one.
    ///
    /// The marker is required, and only <c>--printenv=&lt;marker&gt;</c> is accepted rather than a
    /// separate argument, so a bare <c>--printenv</c> cannot silently produce unwrapped output that the
    /// caller would then fail to locate.
    /// </remarks>
    public static bool TryGetMarker(IReadOnlyList<string> args, out string marker)
    {
        marker = string.Empty;
        if (args is null)
        {
            return false;
        }

        const string prefix = OptionName + "=";
        foreach (var argument in args)
        {
            if (argument is null || !argument.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var candidate = argument[prefix.Length..];
            if (candidate.Length == 0)
            {
                continue;
            }

            marker = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Writes <c>&lt;marker&gt;{...}&lt;marker&gt;</c> to <paramref name="stdout"/> and flushes it.
    /// </summary>
    /// <remarks>
    /// Flushed explicitly because the caller reads this from a pipe and the process may be killed on a
    /// timeout: an unflushed buffer would read as a shell that produced nothing.
    /// </remarks>
    public static async Task WriteAsync(
        string marker,
        TextWriter stdout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stdout);

        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && key.Length > 0)
            {
                variables[key] = entry.Value as string ?? string.Empty;
            }
        }

        var payload = JsonSerializer.Serialize(
            variables,
            DesktopPrintEnvironmentJson.Default.DictionaryStringString);

        cancellationToken.ThrowIfCancellationRequested();
        await stdout.WriteAsync(marker).ConfigureAwait(false);
        await stdout.WriteAsync(payload).ConfigureAwait(false);
        await stdout.WriteAsync(marker).ConfigureAwait(false);
        await stdout.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
