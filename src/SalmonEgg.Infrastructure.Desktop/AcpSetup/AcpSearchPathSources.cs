using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Assembles the search-path sources this installation can actually use, in search order.
/// </summary>
/// <remarks>
/// The two ways of widening the search answer different questions and neither subsumes the other, so the
/// order matters and is fixed here rather than left to registration:
/// <list type="number">
/// <item>The login shell reports the toolchain the user has <em>activated</em>. It leads because that is
/// what their own terminal would run, and it is the only route to a version manager implemented as a shell
/// function — nvm ships no executable, so nothing on disk reveals the node it selected.</item>
/// <item>The on-disk scan reports <em>every</em> installed version, which the shell cannot, because a
/// manager puts only the current one on PATH.</item>
/// </list>
///
/// Which executable answers the printing mode is why this type exists rather than each source
/// self-registering: the capture needs one, and more than one can supply it. The CLI ships as its own
/// package installed independently of the desktop app, so it may be absent — but the running process
/// answers the same mode, so the capture is available regardless of what else is installed. See
/// <see cref="ResolvePrintEnvironmentExecutable"/>.
/// </remarks>
public static class AcpSearchPathSources
{
    /// <summary>The CLI's executable name, which is what an installation puts on PATH.</summary>
    private const string CliExecutableName = "salmon-egg";

    /// <summary>
    /// Creates the sources for this machine.
    /// </summary>
    /// <param name="resolvePrintEnvironmentPath">
    /// Locates an executable implementing the environment-printing mode, or returns null when none is
    /// usable. Injectable so the composition can be tested without depending on what the host machine
    /// happens to have installed.
    /// </param>
    public static IReadOnlyList<IAcpSearchPathSource> Create(
        Func<string?>? resolvePrintEnvironmentPath = null)
    {
        var sources = new List<IAcpSearchPathSource>(2);

        // Windows is not captured: its per-user PATH lives in the registry and the session already holds
        // it, so there is no profile-built PATH to recover and the scan is the whole answer there.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var executable = (resolvePrintEnvironmentPath ?? ResolvePrintEnvironmentExecutable)();
            if (PrintEnvironmentCommandFactory.TryCreate(executable) is { } printEnvironmentCommand)
            {
                sources.Add(new LoginShellSearchPathSource(printEnvironmentCommand));
            }
        }

        sources.Add(new ToolchainScanSearchPathSource());
        return sources;
    }

    /// <summary>
    /// Finds an executable that implements the environment-printing mode.
    /// </summary>
    /// <remarks>
    /// Three candidates, in order:
    /// <list type="number">
    /// <item>A CLI sitting beside this process. A build tree and a portable extraction both put the two
    /// together, and that copy is certain to match this app's version.</item>
    /// <item>A CLI on PATH. Covers the packaged case, where the CLI installs to a system directory (a .deb
    /// owns <c>/usr/bin/salmon-egg</c>) that this process's own directory says nothing about.</item>
    /// <item>This process itself, which answers the same mode.</item>
    /// </list>
    ///
    /// The CLI leads because it is a small single-file executable and the capture starts it inside an
    /// interactive login shell, which the user waits on; starting the whole desktop app there costs more
    /// for an identical answer.
    ///
    /// The last candidate is what makes this chain total. The CLI ships as its own package installed
    /// independently of the desktop app, so a user who installed only the app had no executable answering
    /// the mode — the capture never registered, and the disk scan was the only widening left. That is not a
    /// degraded answer but a missing one for the most common toolchain setup there is: nvm ships no
    /// executable at all, so no amount of scanning reveals the version it activated, and every Node
    /// component reported as absent on a machine that had one.
    /// </remarks>
    internal static string? ResolvePrintEnvironmentExecutable()
    {
        var processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(processDirectory))
        {
            var sibling = Path.Combine(processDirectory, CliExecutableName);
            if (File.Exists(sibling))
            {
                return sibling;
            }
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory, CliExecutableName);
            }
            catch (ArgumentException)
            {
                // PATH entries can hold characters that are invalid in a path on this platform.
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // This process. Checked last so an installed CLI wins, but always present, so the capture is never
        // lost merely because the CLI is not installed.
        var self = Environment.ProcessPath;
        return !string.IsNullOrEmpty(self) && File.Exists(self) ? self : null;
    }
}
