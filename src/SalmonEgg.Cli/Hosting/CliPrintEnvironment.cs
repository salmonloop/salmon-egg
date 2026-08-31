using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Cli.Hosting;

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
/// This path deliberately runs before the CLI builds its service container or performs startup recovery.
/// It reads nothing and writes nothing but stdout, and it is invoked while the app is starting, so
/// touching user data here would make an environment probe a source of configuration side effects.
/// </remarks>
internal static class CliPrintEnvironment
{
    /// <summary>The option that selects this mode. Undocumented: it is an app-internal protocol.</summary>
    internal const string OptionName = "--printenv";

    /// <summary>
    /// True when <paramref name="args"/> requests environment printing, yielding the marker to wrap the
    /// payload in.
    /// </summary>
    /// <remarks>
    /// Matched against raw arguments rather than through the command tree because the container must not
    /// be built for this invocation, and the parser needs it.
    ///
    /// The marker is required, and only <c>--printenv=&lt;marker&gt;</c> is accepted rather than a
    /// separate argument, so a bare <c>--printenv</c> cannot silently produce unwrapped output that the
    /// caller would then fail to locate.
    /// </remarks>
    internal static bool TryGetMarker(IReadOnlyList<string> args, out string marker)
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
    internal static async Task<int> WriteAsync(
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

        var payload = JsonSerializer.Serialize(variables, CliPrintEnvironmentJson.Default.DictionaryStringString);

        cancellationToken.ThrowIfCancellationRequested();
        await stdout.WriteAsync(marker).ConfigureAwait(false);
        await stdout.WriteAsync(payload).ConfigureAwait(false);
        await stdout.WriteAsync(marker).ConfigureAwait(false);
        await stdout.FlushAsync(cancellationToken).ConfigureAwait(false);

        return CliExitCodes.Success;
    }
}
