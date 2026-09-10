using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class DesktopElicitationUriLauncher : IExternalUriLauncher
{
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(3);
    private readonly IPlatformRuntimeCapabilityProbe _runtimeProbe;

    public DesktopElicitationUriLauncher(IPlatformRuntimeCapabilityProbe runtimeProbe)
    {
        _runtimeProbe = runtimeProbe ?? throw new ArgumentNullException(nameof(runtimeProbe));
    }

    // Each additional native target requires its own real handler/isolation gate before advertising.
    public bool IsSupported => OperatingSystem.IsLinux() && _runtimeProbe.IsDesktopProcessHost
        && _runtimeProbe.HasExternalFileOpener;

    public async Task<ExternalUriOpenResult> OpenAsync(ExternalUriTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!IsSupported)
        {
            return ExternalUriOpenResult.Unavailable;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ExternalUriOpenResult.Cancelled;
        }

        try
        {
            var startInfo = PlatformShellService.CreateLaunchProcessStartInfo(target.Uri.AbsoluteUri, _runtimeProbe);
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return ExternalUriOpenResult.Failed;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(OpenTimeout);
            using var readers = new CancellationTokenSource();
            // Browser-handler output may contain the URL. Drain it without retaining or logging it.
            var output = Task.WhenAll(DrainAsync(process.StandardOutput.BaseStream, readers.Token),
                DrainAsync(process.StandardError.BaseStream, readers.Token));
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                await output.WaitAsync(deadline.Token).ConfigureAwait(false);
                return process.ExitCode == 0 ? ExternalUriOpenResult.Opened : ExternalUriOpenResult.Failed;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                return cancellationToken.IsCancellationRequested ? ExternalUriOpenResult.Cancelled : ExternalUriOpenResult.Failed;
            }
            finally
            {
                await CleanupAsync(process, readers, output).ConfigureAwait(false);
            }
        }
        catch
        {
            return cancellationToken.IsCancellationRequested ? ExternalUriOpenResult.Cancelled : ExternalUriOpenResult.Failed;
        }
    }

    private static async Task CleanupAsync(Process process, CancellationTokenSource readers, Task output)
    {
        try
        {
            // Only the short-lived opener is ours. Its browser descendants belong to the user.
            if (!process.HasExited)
            {
                process.Kill();
            }

            using var deadline = new CancellationTokenSource(CleanupTimeout);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        finally
        {
            await readers.CancelAsync().ConfigureAwait(false);
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            await output.ConfigureAwait(false);
        }
    }

    private static async Task DrainAsync(System.IO.Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            await stream.CopyToAsync(System.IO.Stream.Null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested
            && ex is OperationCanceledException or ObjectDisposedException or System.IO.IOException)
        {
            // Closing our read endpoints also releases inherited pipes held by a launched browser.
        }
    }
}
