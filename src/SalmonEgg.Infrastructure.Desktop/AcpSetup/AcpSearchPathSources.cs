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
/// Whether the shell source is available is a deployment question, which is why this type exists rather
/// than each source self-registering: the capture needs an executable that implements the printing mode,
/// and that is the CLI, which ships as its own package installed independently of the desktop app. When it
/// is absent the scan alone still widens the search, so the wizard degrades rather than failing.
/// </remarks>
public static class AcpSearchPathSources
{
    /// <summary>The CLI's executable name, which is what an installation puts on PATH.</summary>
    private const string CliExecutableName = "salmon-egg";

    /// <summary>
    /// Creates the sources for this machine.
    /// </summary>
    /// <param name="resolveCliPath">
    /// Locates the CLI executable, or returns null when none is installed. Injectable so the composition
    /// can be tested without depending on what the host machine happens to have installed.
    /// </param>
    public static IReadOnlyList<IAcpSearchPathSource> Create(Func<string?>? resolveCliPath = null)
    {
        var sources = new List<IAcpSearchPathSource>(2);

        // Windows is not captured: its per-user PATH lives in the registry and the session already holds
        // it, so there is no profile-built PATH to recover and the scan is the whole answer there.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var cliPath = (resolveCliPath ?? ResolveInstalledCliPath)();
            if (PrintEnvironmentCommandFactory.TryCreate(cliPath) is { } printEnvironmentCommand)
            {
                sources.Add(new LoginShellSearchPathSource(printEnvironmentCommand));
            }
        }

        sources.Add(new ToolchainScanSearchPathSource());
        return sources;
    }

    /// <summary>
    /// Finds the installed CLI, preferring one that sits beside this process.
    /// </summary>
    /// <remarks>
    /// The sibling is checked first because a build tree and a portable extraction both put the two
    /// together, and that copy is certain to match this app's version. Falling back to PATH covers the
    /// packaged case, where the CLI installs to a system directory (a .deb owns
    /// <c>/usr/bin/salmon-egg</c>) that this process's own directory says nothing about.
    /// </remarks>
    private static string? ResolveInstalledCliPath()
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

        return null;
    }
}
