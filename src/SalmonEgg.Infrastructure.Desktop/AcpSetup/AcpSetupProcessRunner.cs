using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Outcome of one short-lived helper process: whether it ran at all, its exit code, and its combined
/// output. "Did not start" is distinct from "exited non-zero" because the wizard reports the former as
/// an undetermined probe and the latter as a real failure.
/// </summary>
internal sealed record AcpSetupProcessResult(
    bool Started,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string? FailureDetail)
{
    public bool Succeeded => Started && ExitCode == 0;

    public string CombinedOutput
        => string.IsNullOrEmpty(StandardError)
            ? StandardOutput
            : string.IsNullOrEmpty(StandardOutput)
                ? StandardError
                : StandardOutput + Environment.NewLine + StandardError;
}

/// <summary>
/// Runs the short-lived helper processes the ACP wizard needs — version probes, package-list queries,
/// and package installs — to completion, capturing output.
/// </summary>
/// <remarks>
/// Deliberately separate from <c>StdioTransport</c>: that type owns a long-lived ACP conversation and
/// keeps stdin open, whereas these are fire-and-collect invocations. Sharing one implementation would
/// force one of the two behaviours onto the other.
///
/// Output is capped so a runaway installer cannot grow the buffer without bound; the tail is kept
/// because installer diagnostics land at the end.
/// </remarks>
internal static class AcpSetupProcessRunner
{
    private const int MaxCapturedCharacters = 64 * 1024;

    public static async Task<AcpSetupProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return NotStarted("Executable name was empty.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new OutputAccumulator(onOutputLine);
        var standardError = new OutputAccumulator(onOutputLine);
        process.OutputDataReceived += (_, e) => standardOutput.Append(e.Data);
        process.ErrorDataReceived += (_, e) => standardError.Append(e.Data);

        try
        {
            if (!process.Start())
            {
                return NotStarted($"Failed to start '{fileName}'.");
            }
        }
        catch (Exception ex)
        {
            return NotStarted($"Failed to start '{fileName}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Some launchers (npm on Windows in particular) block on stdin when it stays open; closing it
        // immediately turns that hang into a normal exit.
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The child already closed its end. Nothing to do — the wait below still governs the outcome.
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return new AcpSetupProcessResult(
                Started: true,
                ExitCode: null,
                standardOutput.ToString(),
                standardError.ToString(),
                $"'{fileName}' did not finish within {timeout.TotalSeconds:0}s.");
        }

        return new AcpSetupProcessResult(
            Started: true,
            process.ExitCode,
            standardOutput.ToString(),
            standardError.ToString(),
            FailureDetail: null);
    }

    private static AcpSetupProcessResult NotStarted(string detail)
        => new(Started: false, ExitCode: null, string.Empty, string.Empty, detail);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                // entireProcessTree, because these launchers spawn the real worker as a child: npm runs
                // through a shim, and killing only the shim would leave the worker holding the pipes.
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // Already gone, or the platform refused the kill. Either way the result is already decided.
        }
    }

    /// <summary>
    /// Collects a stream's lines, forwarding each to the caller for progress display while retaining a
    /// bounded tail for the failure surface.
    /// </summary>
    private sealed class OutputAccumulator
    {
        private readonly Action<string>? _onLine;
        private readonly StringBuilder _builder = new();
        private readonly object _gate = new();

        public OutputAccumulator(Action<string>? onLine)
        {
            _onLine = onLine;
        }

        public void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                _builder.AppendLine(line);
                if (_builder.Length > MaxCapturedCharacters)
                {
                    _builder.Remove(0, _builder.Length - MaxCapturedCharacters);
                }
            }

            _onLine?.Invoke(line);
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _builder.ToString().TrimEnd();
            }
        }
    }
}
