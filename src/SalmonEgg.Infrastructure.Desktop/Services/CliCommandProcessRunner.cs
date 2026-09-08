using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Desktop.Services;

internal sealed record CliCommandProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

internal static class CliCommandProcessRunner
{
    internal const int MaxCapturedCharacters = 64 * 1024;

    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(5);

    public static Task<CliCommandProcessResult?> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => RunAsync(startInfo, timeout, cancellationToken, TerminateAndReapAsync);

    internal static async Task<CliCommandProcessResult?> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<Process, Task> terminateAndReapAsync)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(terminateAndReapAsync);
        cancellationToken.ThrowIfCancellationRequested();
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        using var readers = new CancellationTokenSource();
        var stdout = CaptureTailAsync(process.StandardOutput, readers.Token);
        var stderr = CaptureTailAsync(process.StandardError, readers.Token);
        var output = Task.WhenAll(stdout, stderr);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await output.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            timedOut = true;
        }
        finally
        {
            await CleanupAsync(process, readers, output, terminateAndReapAsync, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), timedOut);
    }

    private static async Task CleanupAsync(
        Process process,
        CancellationTokenSource readers,
        Task output,
        Func<Process, Task> terminateAndReapAsync,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            await terminateAndReapAsync(process).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            // Even a refused kill must release and observe both readers. Process.Dispose only releases
            // handles; it does not own these tasks or any child the OS has already reparented.
            await Task.WhenAll(readers.CancelAsync(), output).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = failure is null ? ex : new AggregateException(failure, ex);
        }

        if (failure is not null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The command was cancelled, but process cleanup failed.", failure, cancellationToken);
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static async Task TerminateAndReapAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when ((ex is InvalidOperationException or Win32Exception) && process.HasExited)
        {
            // The command exited between the inspection and the kill.
        }

        using var deadline = new CancellationTokenSource(TerminationTimeout);
        try
        {
            // Process.Dispose releases handles; it neither terminates nor waits for the OS process.
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            throw new IOException("The command did not exit after termination was requested.", ex);
        }
    }

    private static async Task<string> CaptureTailAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var tail = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                tail.Append(buffer, 0, count);
                if (tail.Length > MaxCapturedCharacters)
                {
                    tail.Remove(0, tail.Length - MaxCapturedCharacters);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation still preserves bounded diagnostics already emitted by the child.
        }

        return tail.ToString();
    }
}
