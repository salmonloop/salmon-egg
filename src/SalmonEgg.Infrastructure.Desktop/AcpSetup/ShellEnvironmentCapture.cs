using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Runs the user's login shell so it reports the environment that shell produces.
/// </summary>
/// <remarks>
/// A GUI-launched process inherits the session environment, not the one a shell profile builds, so a
/// version-manager toolchain is invisible to it. This recovers the real environment the way every editor
/// that solves the problem does: run our own executable inside the user's shell and read structured
/// output back, rather than parse the shell's own <c>env</c> text.
///
/// The hazards here are the reason this is its own type. A user's startup files are arbitrary code that
/// this app does not control, and in the wild they hang (tmux attach, pagers, prompts for input), write
/// banners and colour codes onto the same stdout, and exit non-zero while still having produced a usable
/// environment. Every guard below answers one of those:
/// <list type="bullet">
/// <item>stdin is closed, so a startup file that reads input gets EOF instead of blocking. Verified: a
/// capture against an rc file containing a bare <c>read</c> hangs indefinitely without this and returns
/// immediately with it.</item>
/// <item>A timeout kills the shell, because a startup file can block on something that is not stdin.</item>
/// <item>A random per-invocation marker delimits the payload, so surrounding noise cannot be mistaken for
/// it.</item>
/// <item>A non-zero exit is not fatal when the payload parsed, since rc-file failures are common and
/// unrelated to whether the environment was reported.</item>
/// <item>Failure yields null rather than throwing. Losing the captured environment means falling back to
/// the inherited PATH, which is the status quo; blocking the wizard on a shell misconfiguration would be
/// worse than the problem being solved.</item>
/// </list>
/// </remarks>
internal static class ShellEnvironmentCapture
{
    /// <summary>
    /// How long the user's shell gets to report its environment.
    /// </summary>
    /// <remarks>
    /// Startup files that initialize version managers routinely take seconds, and an interactive shell
    /// pays that cost on every capture, so a short timeout would abandon exactly the setups this feature
    /// exists for. VS Code defaults to the same 10 seconds after years of user reports.
    /// </remarks>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Captures the environment produced by <paramref name="shellPath"/>, or null when it could not be
    /// captured.
    /// </summary>
    /// <param name="shellPath">The user's shell. Its file name selects the invocation rules.</param>
    /// <param name="printEnvironmentCommand">
    /// A shell-ready command that prints <c>&lt;marker&gt;{json}&lt;marker&gt;</c>, given the marker this
    /// method generates. Supplied by the caller because only it knows how to invoke this app.
    /// </param>
    internal static async Task<IReadOnlyDictionary<string, string>?> CaptureAsync(
        string shellPath,
        Func<string, string> printEnvironmentCommand,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printEnvironmentCommand);

        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return null;
        }

        var marker = CreateMarker();
        var invocation = AcpShellInvocation.Create(shellPath, printEnvironmentCommand(marker));
        var startInfo = CreateStartInfo(shellPath, invocation);

        var output = await ReadShellOutputAsync(
                startInfo,
                timeout ?? DefaultTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        return output is null ? null : ExtractEnvironment(output, marker);
    }

    /// <summary>
    /// A fresh random marker per invocation.
    /// </summary>
    /// <remarks>
    /// Random rather than fixed because the payload is the shell's own environment: a fixed marker could
    /// appear inside an exported value and turn an honest value into a false delimiter. Hex so it survives
    /// every shell's quoting rules without escaping.
    /// </remarks>
    private static string CreateMarker()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    private static ProcessStartInfo CreateStartInfo(string shellPath, AcpShellInvocation invocation)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = shellPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Redirected and closed immediately after start: a startup file that reads from stdin then
            // sees EOF rather than blocking forever on a terminal that is not there.
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Lets a user's startup files skip work that would hang or pollute a capture.
        startInfo.Environment[AcpShellInvocation.GuardVariableName] = "1";

        // Startup files localize their own messages and colourize prompts. Neither affects the payload,
        // which is delimited JSON, but a predictable locale keeps diagnostics readable when it fails.
        startInfo.Environment["LC_ALL"] = "C";

        return startInfo;
    }

    /// <summary>
    /// Runs the shell to completion and returns its stdout, or null when it could not be run.
    /// </summary>
    /// <remarks>
    /// Output is read concurrently with the wait rather than after it: a shell whose startup files write
    /// more than the pipe buffer holds would block writing while this blocked waiting.
    ///
    /// On timeout the whole process tree is killed. A login shell starts children — that is the point —
    /// and killing only the shell would leave them holding the pipe.
    /// </remarks>
    private static async Task<string?> ReadShellOutputAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or System.IO.IOException or InvalidOperationException)
        {
            // $SHELL can name a file that no longer exists or is not executable.
            return null;
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (Exception exception) when (exception is System.IO.IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The shell already closed its end; the wait below still governs the outcome.
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillTree(process);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        try
        {
            // Awaited after exit so a shell that produced output before failing is still read. The reads
            // complete once the pipes close, which exiting guarantees.
            _ = await standardError.ConfigureAwait(false);
            return await standardOutput.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is System.IO.IOException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // Already gone, or the platform refused. The outcome is already decided either way.
        }
    }

    /// <summary>
    /// Pulls the marker-delimited JSON object out of <paramref name="output"/>.
    /// </summary>
    /// <remarks>
    /// Searched for rather than assumed to be the whole of stdout, because startup files print onto the
    /// same stream. The first marker and the last are used, so a value that happens to contain the marker
    /// cannot truncate the payload.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string>? ExtractEnvironment(string output, string marker)
    {
        if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(marker))
        {
            return null;
        }

        var start = output.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var payloadStart = start + marker.Length;
        var end = output.LastIndexOf(marker, StringComparison.Ordinal);
        if (end <= payloadStart)
        {
            return null;
        }

        try
        {
            var payload = output[payloadStart..end];
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(payload);
            return parsed is null or { Count: 0 } ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
