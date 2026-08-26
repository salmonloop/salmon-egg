using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Contributes the PATH the user's own login shell produces.
/// </summary>
/// <remarks>
/// This is the only route to a version manager implemented as a shell function. nvm is the common case:
/// it ships no executable — <c>command -v nvm</c> finds nothing, only <c>nvm.sh</c> exists on disk — and
/// mutates PATH inside the shell that sources it. No amount of directory scanning can discover the node
/// it activated; asking the shell is the whole mechanism.
///
/// Captured once and reused. A capture spawns an interactive login shell, which runs the user's startup
/// files and routinely takes seconds; repeating that per probe would multiply the cost across every
/// component the wizard inspects, for an answer that does not change while the app runs.
/// </remarks>
public sealed class LoginShellSearchPathSource : IAcpSearchPathSource
{
    private readonly Func<string?> _resolveShellPath;
    private readonly Func<string, string> _printEnvironmentCommand;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<string>? _cached;

    /// <param name="printEnvironmentCommand">
    /// Builds a shell-ready command that prints the environment wrapped in the supplied marker.
    /// </param>
    /// <param name="resolveShellPath">
    /// Yields the user's shell, or null when none can be determined. Injectable so the capture can be
    /// tested without depending on the host's own shell configuration.
    /// </param>
    public LoginShellSearchPathSource(
        Func<string, string> printEnvironmentCommand,
        Func<string?>? resolveShellPath = null)
    {
        _printEnvironmentCommand = printEnvironmentCommand
            ?? throw new ArgumentNullException(nameof(printEnvironmentCommand));
        _resolveShellPath = resolveShellPath ?? ResolveDefaultShellPath;
    }

    public async Task<IReadOnlyList<string>> GetSearchDirectoriesAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cached is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-checked inside the gate: several components are probed in sequence and a concurrent
            // caller must reuse the first capture rather than start a second interactive shell.
            if (_cached is { } existing)
            {
                return existing;
            }

            _cached = await CaptureDirectoriesAsync(cancellationToken).ConfigureAwait(false);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<string>> CaptureDirectoriesAsync(CancellationToken cancellationToken)
    {
        var shellPath = _resolveShellPath();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return Array.Empty<string>();
        }

        var environment = await ShellEnvironmentCapture
            .CaptureAsync(shellPath, _printEnvironmentCommand, timeout: null, cancellationToken)
            .ConfigureAwait(false);

        if (environment is null || !environment.TryGetValue("PATH", out var path) || string.IsNullOrWhiteSpace(path))
        {
            return Array.Empty<string>();
        }

        return SplitPath(path);
    }

    /// <summary>
    /// Splits a captured PATH into directories, preserving order.
    /// </summary>
    /// <remarks>
    /// Order is the shell's answer about precedence, so it is kept: the first entry is what the user's own
    /// terminal would run. Duplicates are left for the consumer to collapse, which it must do anyway
    /// because it merges several sources.
    /// </remarks>
    private static IReadOnlyList<string> SplitPath(string path)
        => path.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The user's shell, preferring <c>SHELL</c> and falling back to the platform default.
    /// </summary>
    /// <remarks>
    /// <c>SHELL</c> is what the user's terminal exports and what every editor reads, but a GUI process may
    /// not have inherited it — which is the same reason this whole feature exists. The fallback keeps the
    /// capture possible in that case rather than abandoning it: <c>/bin/sh</c> still sources a profile, so
    /// it recovers a login PATH even though it misses interactive-only configuration.
    ///
    /// Windows is not captured at all. Its per-user PATH lives in the registry and the session already
    /// holds it, so there is no profile-built PATH to recover: <c>cmd.exe</c> has no login or interactive
    /// startup files, and capturing through it would spend a process to be told what this process already
    /// knows. A Windows user whose toolchain is invisible needs the on-disk scan instead.
    /// </remarks>
    private static string? ResolveDefaultShellPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        var shell = Environment.GetEnvironmentVariable("SHELL");
        return string.IsNullOrWhiteSpace(shell) ? "/bin/sh" : shell;
    }
}
