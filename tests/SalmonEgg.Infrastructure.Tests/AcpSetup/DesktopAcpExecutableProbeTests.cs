using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// Guards the probe's two load-bearing behaviours: resolving a command the way a shell would, and
/// answering "unknown" rather than "absent" when it could not actually look.
/// </summary>
public sealed class DesktopAcpExecutableProbeTests
{
    [Fact]
    public void SupportsProcessProbing_OnDesktop_ShouldBeTrue()
        => Assert.True(new DesktopAcpExecutableProbe().SupportsProcessProbing);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveExecutablePathAsync_WithBlankCommand_ShouldReturnNull(string command)
    {
        var resolved = await new DesktopAcpExecutableProbe()
            .ResolveExecutablePathAsync(command, TestContext.Current.CancellationToken);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveExecutablePathAsync_WithExistingExplicitPath_ShouldReturnFullPath()
    {
        var file = Path.Combine(Path.GetTempPath(), $"acp-probe-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(file, "#!/bin/sh\n", TestContext.Current.CancellationToken);
        try
        {
            var resolved = await new DesktopAcpExecutableProbe()
                .ResolveExecutablePathAsync(file, TestContext.Current.CancellationToken);

            Assert.Equal(Path.GetFullPath(file), resolved);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ResolveExecutablePathAsync_WithMissingExplicitPath_ShouldReturnNull()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"acp-absent-{Guid.NewGuid():N}");

        var resolved = await new DesktopAcpExecutableProbe()
            .ResolveExecutablePathAsync(missing, TestContext.Current.CancellationToken);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveExecutablePathAsync_WithNameNotOnPath_ShouldReturnNull()
    {
        var resolved = await new DesktopAcpExecutableProbe()
            .ResolveExecutablePathAsync($"acp-nonexistent-{Guid.NewGuid():N}", TestContext.Current.CancellationToken);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveExecutablePathAsync_WhenCancelled_ShouldThrow()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DesktopAcpExecutableProbe().ResolveExecutablePathAsync("npm", cts.Token));
    }

    [Fact]
    public async Task ReadVersionAsync_WithUnresolvableCommand_ShouldReturnNull()
    {
        var version = await new DesktopAcpExecutableProbe()
            .ReadVersionAsync($"acp-nonexistent-{Guid.NewGuid():N}", new[] { "--version" }, TestContext.Current.CancellationToken);

        Assert.Null(version);
    }

    [Fact]
    public async Task ReadVersionAsync_WithNullArguments_ShouldReturnNull()
    {
        var version = await new DesktopAcpExecutableProbe()
            .ReadVersionAsync("npm", null!, TestContext.Current.CancellationToken);

        Assert.Null(version);
    }

    /// <summary>
    /// A blank package coordinate is unanswerable, not absent: reporting false here would make the
    /// wizard advertise an install for a component it never asked about.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LocateGlobalPackageAsync_WithBlankNodePackageId_ShouldReturnUnknown(string packageId)
    {
        var installed = await new DesktopAcpExecutableProbe().LocateGlobalPackageAsync(
            AcpDistributionKind.Npx,
            packageId,
            AcpPackageManagerCandidates.Exact("npm"),
            TestContext.Current.CancellationToken);

        Assert.Null(installed.IsInstalled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LocateGlobalPackageAsync_WithBlankUvToolId_ShouldReturnUnknown(string packageId)
    {
        var installed = await new DesktopAcpExecutableProbe().LocateGlobalPackageAsync(
            AcpDistributionKind.Uvx,
            packageId,
            AcpPackageManagerCandidates.Exact("uv"),
            TestContext.Current.CancellationToken);

        Assert.Null(installed.IsInstalled);
    }

    /// <summary>
    /// A distribution with no package manager has nothing to ask, so the query is unanswerable rather
    /// than absent — the same rule that keeps an unreachable manager from reading as "not installed".
    /// </summary>
    [Theory]
    [InlineData(AcpDistributionKind.BuiltIn)]
    [InlineData(AcpDistributionKind.Binary)]
    public async Task LocateGlobalPackageAsync_WithNonPackageDistribution_ShouldReturnUnknown(
        AcpDistributionKind distribution)
    {
        var installed = await new DesktopAcpExecutableProbe().LocateGlobalPackageAsync(
            distribution,
            "some-package",
            AcpPackageManagerCandidates.Exact("npm"),
            TestContext.Current.CancellationToken);

        Assert.Null(installed.IsInstalled);
    }

    [Theory]
    [InlineData("@agentclientprotocol/codex-acp@1.6.2", "@agentclientprotocol/codex-acp")]
    [InlineData("@scope/pkg", "@scope/pkg")]
    [InlineData("plain-package@1.2.3", "plain-package")]
    [InlineData("plain-package", "plain-package")]
    [InlineData("  spaced@0.1.0  ", "spaced")]
    public void StripVersionSuffix_ShouldDropVersionAndKeepScope(string packageId, string expected)
        => Assert.Equal(expected, DesktopAcpExecutableProbe.StripVersionSuffix(packageId));

    /// <summary>
    /// Several directories on PATH holding the same command name must all be reported, in PATH order.
    /// A shell runs the first and never mentions the rest, so the wizard is the only thing that can tell
    /// the user a second install exists.
    /// </summary>
    [Fact]
    public async Task ResolveExecutableCandidatesAsync_WithSeveralInstalls_ShouldReturnAllInPathOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "acp-candidates-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root, "first");
        var second = Path.Combine(root, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var command = "acp-fake-" + Guid.NewGuid().ToString("N");
        var firstPath = Path.Combine(first, command);
        var secondPath = Path.Combine(second, command);
        await File.WriteAllTextAsync(firstPath, "#!/bin/sh\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(secondPath, "#!/bin/sh\n", TestContext.Current.CancellationToken);
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            Environment.SetEnvironmentVariable("PATH", first + separator + second);

            var candidates = await new DesktopAcpExecutableProbe()
                .ResolveExecutableCandidatesAsync(command, TestContext.Current.CancellationToken);

            Assert.Equal(2, candidates.Count);
            Assert.Equal(firstPath, candidates[0]);
            Assert.Equal(secondPath, candidates[1]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A PATH that lists one directory several times — which is ordinary, shell profiles append
    /// repeatedly — must yield one candidate. Otherwise a machine with a single install is told it has a
    /// choice between a path and itself.
    /// </summary>
    [Fact]
    public async Task ResolveExecutableCandidatesAsync_WithRepeatedPathEntries_ShouldDeduplicate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "acp-dupe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var command = "acp-fake-" + Guid.NewGuid().ToString("N");
        var executable = Path.Combine(directory, command);
        await File.WriteAllTextAsync(executable, "#!/bin/sh\n", TestContext.Current.CancellationToken);
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            Environment.SetEnvironmentVariable(
                "PATH",
                string.Join(separator, directory, directory, directory));

            var candidates = await new DesktopAcpExecutableProbe()
                .ResolveExecutableCandidatesAsync(command, TestContext.Current.CancellationToken);

            Assert.Equal(executable, Assert.Single(candidates));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveExecutableCandidatesAsync_WhenNothingMatches_ShouldReturnEmpty()
    {
        var candidates = await new DesktopAcpExecutableProbe()
            .ResolveExecutableCandidatesAsync(
                "acp-nonexistent-" + Guid.NewGuid().ToString("N"),
                TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
    }

    /// <summary>
    /// The package name is matched as a whole path segment. A substring test reports a package as
    /// installed whenever an unrelated package merely contains its name, and a false "installed" is the
    /// worst answer available: the wizard skips the install and fails at launch instead.
    /// </summary>
    [Theory]
    // npm --parseable prints one path per line ending in the package directory.
    [InlineData("/n/lib/node_modules/cline", "cline", true)]
    [InlineData("/n/lib/node_modules/@agentclientprotocol/codex-acp", "@agentclientprotocol/codex-acp", true)]
    // A different package that merely contains the name must not match.
    [InlineData("/n/lib/node_modules/my-cline-fork", "cline", false)]
    [InlineData("/n/lib/node_modules/claude-adapter", "claude", false)]
    // A scoped package must not be matched by its bare name, nor by a different scope.
    [InlineData("/n/lib/node_modules/@other/codex-acp", "@agentclientprotocol/codex-acp", false)]
    // uv tool list prints "name version".
    [InlineData("some-tool 1.2.3", "some-tool", true)]
    [InlineData("some-tool-extended 1.2.3", "some-tool", false)]
    public void FindPackageLocation_MatchesWholeSegments_NotSubstrings(
        string output,
        string packageName,
        bool expectedMatch)
    {
        var location = DesktopAcpExecutableProbe.FindPackageLocation(output, packageName);

        Assert.Equal(expectedMatch, location is not null);
    }

    /// <summary>
    /// A version read must search the same directories a resolution does.
    /// </summary>
    /// <remarks>
    /// The widened search is this feature's whole reason for existing: a GUI process cannot see the PATH a
    /// shell profile builds, so a version-manager toolchain is reachable only through the sources. A version
    /// read that consulted the inherited PATH alone therefore failed on exactly the machines the sources were
    /// added for, and the row showed a component as installed with no version beside it.
    /// </remarks>
    [Fact]
    public async Task ReadVersionAsync_ForCommandOnlyASourceCanSee_ShouldReadTheVersion()
    {
        using var toolchain = ExecutableFixture.Printing("9.9.9");

        var version = await new DesktopAcpExecutableProbe(new[] { toolchain.Source })
            .ReadVersionAsync(toolchain.Command, new[] { "--version" }, TestContext.Current.CancellationToken);

        Assert.Equal("9.9.9", version);
    }

    /// <summary>
    /// Reverse verification: without the source, the same command is unreachable — so the test above
    /// passes because of the widened search rather than because the command happened to be on PATH.
    /// </summary>
    [Fact]
    public async Task ReadVersionAsync_ForTheSameCommandWithoutTheSource_ShouldReturnNull()
    {
        using var toolchain = ExecutableFixture.Printing("9.9.9");

        var version = await new DesktopAcpExecutableProbe()
            .ReadVersionAsync(toolchain.Command, new[] { "--version" }, TestContext.Current.CancellationToken);

        Assert.Null(version);
    }

    /// <summary>
    /// A package query must reach a manager only a source can see, for the same reason.
    /// </summary>
    /// <remarks>
    /// This failure was worse than the version one: the query reported <c>Unknown</c> with no executable
    /// named, which the wizard renders as "could not determine". A user whose npm lives in an nvm directory
    /// therefore got an undetermined verdict for every package-detected component, with nothing on screen
    /// pointing at the cause.
    /// </remarks>
    [Fact]
    public async Task LocateGlobalPackageAsync_ForManagerOnlyASourceCanSee_ShouldQueryIt()
    {
        using var manager = ExecutableFixture.Printing("/fake/lib/node_modules/probe-pkg");

        var result = await new DesktopAcpExecutableProbe(new[] { manager.Source }).LocateGlobalPackageAsync(
            AcpDistributionKind.Npx,
            "probe-pkg",
            AcpPackageManagerCandidates.Exact(manager.Command),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsInstalled);
        Assert.Equal(manager.Path, result.QueryExecutablePath);
    }

    /// <summary>
    /// Reverse verification for the query: the same manager is unreachable without the source.
    /// </summary>
    [Fact]
    public async Task LocateGlobalPackageAsync_ForTheSameManagerWithoutTheSource_ShouldReturnUnknown()
    {
        using var manager = ExecutableFixture.Printing("/fake/lib/node_modules/probe-pkg");

        var result = await new DesktopAcpExecutableProbe().LocateGlobalPackageAsync(
            AcpDistributionKind.Npx,
            "probe-pkg",
            AcpPackageManagerCandidates.Exact(manager.Command),
            TestContext.Current.CancellationToken);

        Assert.Null(result.IsInstalled);
        Assert.Null(result.QueryExecutablePath);
    }

    [Fact]
    public void FindPackageLocation_ReturnsTheMatchedPath_SoTheToolchainCanBeNamed()
    {
        const string output = """
            /home/u/.nvm/versions/node/v24.14.1/lib
            /home/u/.nvm/versions/node/v24.14.1/lib/node_modules/@agentclientprotocol/codex-acp
            """;

        var location = DesktopAcpExecutableProbe.FindPackageLocation(output, "@agentclientprotocol/codex-acp");

        Assert.Equal(
            "/home/u/.nvm/versions/node/v24.14.1/lib/node_modules/@agentclientprotocol/codex-acp",
            location);
    }
}
