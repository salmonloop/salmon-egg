using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        var resolved = await new DesktopAcpExecutableProbe().ResolveExecutablePathAsync(command);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveExecutablePathAsync_WithExistingExplicitPath_ShouldReturnFullPath()
    {
        var file = Path.Combine(Path.GetTempPath(), $"acp-probe-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(file, "#!/bin/sh\n");
        try
        {
            var resolved = await new DesktopAcpExecutableProbe().ResolveExecutablePathAsync(file);

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

        var resolved = await new DesktopAcpExecutableProbe().ResolveExecutablePathAsync(missing);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveExecutablePathAsync_WithNameNotOnPath_ShouldReturnNull()
    {
        var resolved = await new DesktopAcpExecutableProbe()
            .ResolveExecutablePathAsync($"acp-nonexistent-{Guid.NewGuid():N}");

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
            .ReadVersionAsync($"acp-nonexistent-{Guid.NewGuid():N}", new[] { "--version" });

        Assert.Null(version);
    }

    [Fact]
    public async Task ReadVersionAsync_WithNullArguments_ShouldReturnNull()
    {
        var version = await new DesktopAcpExecutableProbe().ReadVersionAsync("npm", null!);

        Assert.Null(version);
    }

    /// <summary>
    /// A blank package coordinate is unanswerable, not absent: reporting false here would make the
    /// wizard advertise an install for a component it never asked about.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IsGlobalNodePackageInstalledAsync_WithBlankPackageId_ShouldReturnNull(string packageId)
    {
        var installed = await new DesktopAcpExecutableProbe().IsGlobalNodePackageInstalledAsync(packageId);

        Assert.Null(installed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IsGlobalUvToolInstalledAsync_WithBlankPackageId_ShouldReturnNull(string packageId)
    {
        var installed = await new DesktopAcpExecutableProbe().IsGlobalUvToolInstalledAsync(packageId);

        Assert.Null(installed);
    }

    [Theory]
    [InlineData("@agentclientprotocol/codex-acp@1.6.2", "@agentclientprotocol/codex-acp")]
    [InlineData("@scope/pkg", "@scope/pkg")]
    [InlineData("plain-package@1.2.3", "plain-package")]
    [InlineData("plain-package", "plain-package")]
    [InlineData("  spaced@0.1.0  ", "spaced")]
    public void StripVersionSuffix_ShouldDropVersionAndKeepScope(string packageId, string expected)
        => Assert.Equal(expected, DesktopAcpExecutableProbe.StripVersionSuffix(packageId));
}
