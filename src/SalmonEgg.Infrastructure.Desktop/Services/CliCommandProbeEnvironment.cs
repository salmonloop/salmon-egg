using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Desktop.Services;

/// <summary>
/// What the resolved executable said about itself.
/// </summary>
/// <param name="Version">The version it printed, or <c>null</c> when it did not print a usable one.</param>
/// <param name="FailureDetail">Why nothing usable came back, when that is the case.</param>
public sealed record CliVersionProbe(string? Version, string? FailureDetail)
{
    public static CliVersionProbe Success(string version) => new(version, null);

    public static CliVersionProbe Failure(string detail) => new(null, detail);
}

/// <summary>
/// The machine facts a PATH lookup depends on, behind one seam.
/// </summary>
/// <remarks>
/// All four are things the process cannot control and a test cannot arrange: the PATH variable, whether a
/// file is there, where a symlink leads, and what running an executable prints. Putting them behind one
/// interface is what lets the resolution logic — which is where the interesting mistakes live — be tested
/// without a real PATH, real files, or a real child process.
/// </remarks>
public interface ICliCommandProbeEnvironment
{
    /// <summary>The PATH variable as this process sees it, or <c>null</c> when it has none.</summary>
    string? GetSearchPath();

    /// <summary>The separator this platform uses between PATH entries.</summary>
    char SearchPathSeparator { get; }

    /// <summary>The command's file name on this platform.</summary>
    string CommandFileName { get; }

    bool FileExists(string path);

    /// <summary>
    /// Where a symlink leads, following the whole chain, or <c>null</c> when the path is not a link.
    /// </summary>
    string? ResolveLinkTarget(string path);

    Task<CliVersionProbe> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken);
}

/// <summary>
/// The real machine.
/// </summary>
public sealed class SystemCliCommandProbeEnvironment : ICliCommandProbeEnvironment
{
    // A self-contained single-file executable extracts its native libraries on first run, so the first
    // invocation after an install is measurably slower than every later one. Ten seconds is chosen to be
    // longer than that rather than tight: a timeout here reports "the command would not say which version
    // it is", which is a worse answer than waiting.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    public string? GetSearchPath() => Environment.GetEnvironmentVariable("PATH");

    public char SearchPathSeparator => Path.PathSeparator;

    public string CommandFileName => OperatingSystem.IsWindows()
        ? Domain.Models.Cli.CliCommandNames.WindowsFileName
        : Domain.Models.Cli.CliCommandNames.Command;

    public bool FileExists(string path) => File.Exists(path);

    public string? ResolveLinkTarget(string path)
    {
        try
        {
            // returnFinalTarget follows the whole chain: a link to a link to an app bundle is exactly the
            // shape a stale macOS installation leaves behind, and reporting the first hop would hide it.
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName;
        }
        catch (IOException)
        {
            // A broken chain, or a path that stopped existing between the check and here. Not knowing where
            // it leads is not the same as it not being a link, but neither is actionable differently.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<CliVersionProbe> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--version");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return CliVersionProbe.Failure("the operating system did not start the process");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The whole tree: the single-file host extracts to a temporary directory and can leave a
                // child behind, and a probe must not outlive its own timeout.
                TryKill(process);
                return CliVersionProbe.Failure($"the command did not exit within {ProbeTimeout.TotalSeconds:0} seconds");
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return CliVersionProbe.Failure($"the command exited with code {process.ExitCode}");
            }

            var version = stdout.Trim();
            return string.IsNullOrEmpty(version)
                ? CliVersionProbe.Failure("the command printed no version")
                : CliVersionProbe.Success(version);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return CliVersionProbe.Failure(ex.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // It exited on its own between the timeout and here, or the platform refused. Either way the
            // probe's answer is already decided.
        }
    }
}
