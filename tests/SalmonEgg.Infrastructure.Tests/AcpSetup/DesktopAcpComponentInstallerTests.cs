using System;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// Guards the installer's decisions before any package manager runs: which distributions it will
/// install at all, which launcher each one maps to, and that an unavailable launcher is reported as a
/// failure instead of a silent no-op.
/// </summary>
public sealed class DesktopAcpComponentInstallerTests
{
    [Fact]
    public void Constructor_WithNullProbe_ShouldThrow()
        => Assert.Throws<ArgumentNullException>(() => new DesktopAcpComponentInstaller(null!));

    [Fact]
    public async Task InstallAsync_WithNullComponent_ShouldThrow()
    {
        var installer = new DesktopAcpComponentInstaller(new StubAcpExecutableProbe());

        await Assert.ThrowsAsync<ArgumentNullException>(() => installer.InstallAsync(null!));
    }

    [Fact]
    public async Task InstallAsync_ForBinaryDistribution_ShouldFailWithoutRunningAnything()
    {
        var probe = new StubAcpExecutableProbe();
        var installer = new DesktopAcpComponentInstaller(probe);

        var result = await installer.InstallAsync(AcpSetupFixtures.BinaryComponent());

        Assert.False(result.IsSuccess);
        Assert.Null(result.ExitCode);
        Assert.Contains("manually", result.ErrorDetail);
        Assert.Empty(probe.ResolveRequests);
    }

    [Fact]
    public async Task InstallAsync_WhenLauncherMissingFromPath_ShouldFailWithLauncherName()
    {
        var probe = new StubAcpExecutableProbe();
        probe.SetResolvedPath("npm", null);
        var installer = new DesktopAcpComponentInstaller(probe);

        var result = await installer.InstallAsync(AcpSetupFixtures.NpxComponent());

        Assert.False(result.IsSuccess);
        Assert.Null(result.ExitCode);
        Assert.Contains("npm", result.ErrorDetail);
        Assert.Contains("PATH", result.ErrorDetail);
    }

    [Fact]
    public async Task InstallAsync_ForNpxDistribution_ShouldResolveNpmLauncher()
    {
        var probe = new StubAcpExecutableProbe();
        probe.SetResolvedPath("npm", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var installer = new DesktopAcpComponentInstaller(probe);

        var result = await installer.InstallAsync(AcpSetupFixtures.NpxComponent());

        Assert.Equal(new[] { "npm" }, probe.ResolveRequests);
        // The resolved path does not exist, so the attempt fails at start rather than reporting success.
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task InstallAsync_ForUvxDistribution_ShouldResolveUvLauncher()
    {
        var probe = new StubAcpExecutableProbe();
        probe.SetResolvedPath("uv", null);
        var installer = new DesktopAcpComponentInstaller(probe);

        var result = await installer.InstallAsync(AcpSetupFixtures.UvxComponent());

        Assert.Equal(new[] { "uv" }, probe.ResolveRequests);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task InstallAsync_ForComponentWithoutPackageId_ShouldFailAsManual()
    {
        var component = new AcpComponentDescriptor
        {
            Id = "adapter.no-package",
            DisplayName = "No Package",
            Distribution = AcpDistributionKind.Npx,
            PackageId = string.Empty
        };
        var probe = new StubAcpExecutableProbe();
        var installer = new DesktopAcpComponentInstaller(probe);

        var result = await installer.InstallAsync(component);

        Assert.False(result.IsSuccess);
        Assert.Empty(probe.ResolveRequests);
    }

    [Fact]
    public void SupportsAutomaticInstall_OnDesktop_ShouldBeTrue()
        => Assert.True(new DesktopAcpComponentInstaller(new StubAcpExecutableProbe()).SupportsAutomaticInstall);
}
