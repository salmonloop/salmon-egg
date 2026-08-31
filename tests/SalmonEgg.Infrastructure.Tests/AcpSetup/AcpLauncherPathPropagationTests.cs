using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// Guards the contract that a launcher resolved outside the inherited PATH can still run: its own
/// directory is placed on the child's PATH.
/// </summary>
/// <remarks>
/// This is the failure a GUI-launched app hits. A desktop process started from Finder, a .desktop file,
/// or Explorer inherits the session PATH, not the PATH a shell profile builds — so a version-manager
/// toolchain (nvm, fnm, volta, asdf) is invisible to it. The wizard's answer is to let the user name an
/// absolute path, but naming the launcher is not enough on its own: every Node CLI begins with
/// <c>#!/usr/bin/env node</c>, so the launcher resolves and then dies with exit 127 —
/// "/usr/bin/env: 'node': No such file or directory" — because its sibling <c>node</c> is still off PATH.
///
/// Verified against a real nvm install: invoking an absolute-path npm/npx/gemini with the systemd user
/// session PATH exits 127 for exactly that reason. Prepending the launcher's own directory is what makes
/// the sibling interpreter reachable, which is why these tests assert on PATH content rather than on the
/// launcher merely resolving.
/// </remarks>
public sealed class AcpLauncherPathPropagationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "acp-launcher-path-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A launcher given by absolute path must find its siblings: the directory it lives in is prepended
    /// to the child's PATH.
    /// </summary>
    [Fact]
    public void CreateProcessStartInfo_WithLauncherOutsidePath_ShouldPrependItsOwnDirectory()
    {
        var launcherDirectory = Path.Combine(_root, "toolchain", "bin");
        Directory.CreateDirectory(launcherDirectory);
        var launcher = Path.Combine(launcherDirectory, "fake-launcher");
        File.WriteAllText(launcher, "#!/bin/sh\n");

        var startInfo = AcpSetupProcessRunner.CreateProcessStartInfo(launcher, Array.Empty<string>());

        var path = startInfo.Environment["PATH"] ?? string.Empty;
        Assert.StartsWith(launcherDirectory, path, StringComparison.Ordinal);
    }

    /// <summary>
    /// Prepended, not replaced: the inherited PATH still has to work, because a launcher routinely shells
    /// out to tools that live elsewhere (git, a system compiler) during an install.
    /// </summary>
    [Fact]
    public void CreateProcessStartInfo_WithLauncherOutsidePath_ShouldKeepInheritedPath()
    {
        var launcherDirectory = Path.Combine(_root, "keep-inherited");
        Directory.CreateDirectory(launcherDirectory);
        var launcher = Path.Combine(launcherDirectory, "fake-launcher");
        File.WriteAllText(launcher, "#!/bin/sh\n");
        var inherited = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        var startInfo = AcpSetupProcessRunner.CreateProcessStartInfo(launcher, Array.Empty<string>());

        var path = startInfo.Environment["PATH"] ?? string.Empty;
        Assert.Contains(inherited, path, StringComparison.Ordinal);
    }

    /// <summary>
    /// A directory already on PATH must not be prepended again: repeated entries accumulate every time
    /// the wizard runs a probe, and an unbounded PATH is its own failure on Windows.
    /// </summary>
    [Fact]
    public void CreateProcessStartInfo_WithLauncherAlreadyOnPath_ShouldNotDuplicateTheEntry()
    {
        var launcherDirectory = Path.Combine(_root, "already-on-path");
        Directory.CreateDirectory(launcherDirectory);
        var launcher = Path.Combine(launcherDirectory, "fake-launcher");
        File.WriteAllText(launcher, "#!/bin/sh\n");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var separator = OperatingSystem.IsWindows() ? ';' : ':';

        try
        {
            Environment.SetEnvironmentVariable("PATH", launcherDirectory + separator + "/usr/bin");

            var startInfo = AcpSetupProcessRunner.CreateProcessStartInfo(launcher, Array.Empty<string>());

            var path = startInfo.Environment["PATH"] ?? string.Empty;
            Assert.Equal(launcherDirectory + separator + "/usr/bin", path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    /// <summary>
    /// The real end of the chain: a launcher whose interpreter is a sibling must actually run. This is
    /// the exit-127 failure expressed as a behaviour rather than as a PATH assertion.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithSiblingInterpreterOffPath_ShouldStillRunTheLauncher()
    {
        if (OperatingSystem.IsWindows())
        {
            // The shebang mechanism this exercises is POSIX-only.
            return;
        }

        var toolchain = Path.Combine(_root, "sibling", "bin");
        Directory.CreateDirectory(toolchain);

        // The sibling "interpreter" the launcher reaches through `env`, mirroring how every Node CLI
        // finds `node`.
        var interpreter = Path.Combine(toolchain, "fake-interpreter");
        await File.WriteAllTextAsync(
            interpreter,
            "#!/bin/sh\necho INTERPRETER-RAN\n",
            TestContext.Current.CancellationToken);
        SetExecutable(interpreter);

        var launcher = Path.Combine(toolchain, "fake-launcher");
        await File.WriteAllTextAsync(
            launcher,
            "#!/usr/bin/env fake-interpreter\n",
            TestContext.Current.CancellationToken);
        SetExecutable(launcher);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            // A GUI session PATH: the toolchain directory is absent from it.
            Environment.SetEnvironmentVariable("PATH", "/usr/bin:/bin");

            var result = await AcpSetupProcessRunner.RunAsync(
                launcher,
                Array.Empty<string>(),
                TimeSpan.FromSeconds(30),
                onOutputLine: null,
                TestContext.Current.CancellationToken);

            Assert.True(result.Started);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("INTERPRETER-RAN", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    private static void SetExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
