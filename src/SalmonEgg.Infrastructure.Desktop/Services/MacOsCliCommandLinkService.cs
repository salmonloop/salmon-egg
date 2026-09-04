using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Cli;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Desktop.Services;

/// <summary>
/// Links this app's bundled command into <c>/usr/local/bin</c>, with an administrator prompt.
/// </summary>
/// <remarks>
/// macOS is the only platform where the app owns this. Its .pkg carries a postinstall that does the same
/// thing, but a .dmg is dragged — there is no install phase at all — so without this the .dmg's users have
/// no way to reach the command. Windows and Linux installers own their registration and this service
/// refuses there: two owners writing one path means whichever uninstall runs second leaves a command
/// pointing at a deleted app.
///
/// <c>/usr/local/bin</c> because macOS ships it in /etc/paths, so a link there needs no shell profile edit.
/// Writing it needs root, hence the authorization prompt; the user cancelling is a normal outcome, not an
/// error, and is reported as such so the UI does not show a failure for a deliberate choice.
/// </remarks>
public sealed class MacOsCliCommandLinkService : ICliCommandLinkService
{
    private const string LinkDirectory = "/usr/local/bin";

    // Long enough for the user to read the prompt and type a password. Nothing is retried on timeout: a
    // second prompt after the first one is still on screen is worse than reporting that nothing happened.
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(2);

    private readonly IPlatformCapabilityService _capabilities;
    private readonly Func<string> _resolveBaseDirectory;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, CancellationToken, Task<PrivilegedShellResult>> _runPrivileged;

    public MacOsCliCommandLinkService(IPlatformCapabilityService capabilities)
        : this(
            capabilities,
            () => AppContext.BaseDirectory,
            File.Exists,
            RunOsaScriptAsync)
    {
    }

    internal MacOsCliCommandLinkService(
        IPlatformCapabilityService capabilities,
        Func<string> resolveBaseDirectory,
        Func<string, bool> fileExists,
        Func<string, CancellationToken, Task<PrivilegedShellResult>> runPrivileged)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _resolveBaseDirectory = resolveBaseDirectory ?? throw new ArgumentNullException(nameof(resolveBaseDirectory));
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _runPrivileged = runPrivileged ?? throw new ArgumentNullException(nameof(runPrivileged));
    }

    public bool IsSupported => _capabilities.SupportsCliCommandLinking;

    /// <summary>The path the link occupies, so the UI can name it before asking for authorization.</summary>
    public static string LinkPath { get; } = Path.Combine(LinkDirectory, CliCommandNames.Command);

    public async Task<CliCommandLinkResult> LinkAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return CliCommandLinkResult.Unsupported();
        }

        var source = ResolveBundledCommandPath();
        if (source is null)
        {
            return CliCommandLinkResult.Failed(
                "this app does not carry a salmon-egg command, so there is nothing to link");
        }

        var command = MacOsPrivilegedShellScript.BuildLinkCommand(source, LinkPath, LinkDirectory);
        return Translate(await _runPrivileged(command, cancellationToken).ConfigureAwait(false), CliCommandLinkOutcome.Linked);
    }

    public async Task<CliCommandLinkResult> UnlinkAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return CliCommandLinkResult.Unsupported();
        }

        var command = MacOsPrivilegedShellScript.BuildUnlinkCommand(LinkPath);
        return Translate(await _runPrivileged(command, cancellationToken).ConfigureAwait(false), CliCommandLinkOutcome.Unlinked);
    }

    /// <summary>
    /// Finds the command inside the running app.
    /// </summary>
    /// <remarks>
    /// Both bundle areas are probed, in the same order and for the same reason as the pkg's postinstall:
    /// Uno's bundle generator sends the apphost and dylibs to Contents/MacOS and everything else to
    /// Contents/Resources, and a cli/ subdirectory holding one extension-less Mach-O matches neither pattern
    /// exactly. The unpackaged publish layout is probed last so a developer running out of a publish
    /// directory gets the same behaviour as an installed app.
    /// </remarks>
    private string? ResolveBundledCommandPath()
    {
        var baseDirectory = _resolveBaseDirectory();
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        var bundleRoot = FindBundleRoot(baseDirectory);
        if (bundleRoot is not null)
        {
            foreach (var area in new[] { "MacOS", "Resources" })
            {
                var candidate = Path.Combine(bundleRoot, "Contents", area, CliCommandNames.PayloadDirectory, CliCommandNames.Command);
                if (_fileExists(candidate))
                {
                    return candidate;
                }
            }
        }

        var unpackaged = Path.Combine(baseDirectory, CliCommandNames.PayloadDirectory, CliCommandNames.Command);
        return _fileExists(unpackaged) ? unpackaged : null;
    }

    /// <summary>Walks up from a directory inside the bundle to the <c>.app</c> itself.</summary>
    private static string? FindBundleRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (directory.Name.EndsWith(".app", StringComparison.Ordinal))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static CliCommandLinkResult Translate(PrivilegedShellResult result, CliCommandLinkOutcome success) =>
        result.Status switch
        {
            PrivilegedShellStatus.Succeeded when success == CliCommandLinkOutcome.Linked => CliCommandLinkResult.Linked(),
            PrivilegedShellStatus.Succeeded => CliCommandLinkResult.Unlinked(),
            PrivilegedShellStatus.Cancelled => CliCommandLinkResult.Cancelled(),
            _ => CliCommandLinkResult.Failed(result.Detail ?? "the authorized command failed"),
        };

    private static async Task<PrivilegedShellResult> RunOsaScriptAsync(string shellCommand, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/osascript")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(MacOsPrivilegedShellScript.BuildOsaScriptStatement(shellCommand));

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return PrivilegedShellResult.Failed("the operating system did not start osascript");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(AuthorizationTimeout);

            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return PrivilegedShellResult.Failed("the authorization prompt was not answered in time");
            }

            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode == 0)
            {
                return PrivilegedShellResult.Succeeded();
            }

            // osascript reports a dismissed authorization dialog as AppleScript error -128, the same code
            // any user cancellation produces. Treating it as a failure would show an error for a deliberate
            // choice, so it is matched on the code rather than on the localized message text.
            return stderr.Contains("-128", StringComparison.Ordinal)
                ? PrivilegedShellResult.Cancelled()
                : PrivilegedShellResult.Failed(string.IsNullOrWhiteSpace(stderr) ? $"osascript exited with {process.ExitCode}" : stderr.Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return PrivilegedShellResult.Failed(ex.Message);
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
            // Already gone, or the platform refused. The outcome is decided either way.
        }
    }
}

/// <summary>How an authorized shell command ended.</summary>
public enum PrivilegedShellStatus
{
    Succeeded,
    Cancelled,
    Failed,
}

/// <summary>The outcome of running a shell command through the authorization prompt.</summary>
public sealed record PrivilegedShellResult(PrivilegedShellStatus Status, string? Detail = null)
{
    public static PrivilegedShellResult Succeeded() => new(PrivilegedShellStatus.Succeeded);

    public static PrivilegedShellResult Cancelled() => new(PrivilegedShellStatus.Cancelled);

    public static PrivilegedShellResult Failed(string detail) => new(PrivilegedShellStatus.Failed, detail);
}
