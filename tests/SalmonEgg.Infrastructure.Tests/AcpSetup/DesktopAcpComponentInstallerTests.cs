using System;
using System.Collections.Generic;
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

        await Assert.ThrowsAsync<ArgumentNullException>(() => installer.InstallAsync(null!, onOutput: null, overrides: null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InstallAsync_ForBinaryDistribution_ShouldFailWithoutRunningAnything()
    {
        var probe = new StubAcpExecutableProbe();
        var installer = new DesktopAcpComponentInstaller(probe);

        var result = await installer.InstallAsync(AcpSetupFixtures.BinaryComponent(), onOutput: null, overrides: null, TestContext.Current.CancellationToken);

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

        var result = await installer.InstallAsync(AcpSetupFixtures.NpxComponent(), onOutput: null, overrides: null, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(result.ExitCode);
        Assert.Contains("npm", result.ErrorDetail);
        Assert.Contains("PATH", result.ErrorDetail);
    }

    /// <summary>
    /// An absent package manager is a missing toolchain, and the result must say so in a form the
    /// presentation layer can localize.
    /// </summary>
    /// <remarks>
    /// What shipped was the raw detail above and nothing else, so the user read an untranslated sentence
    /// naming <c>npm</c> — an executable they may never have installed deliberately — rather than being
    /// told they need Node.js. The detail stays for diagnostics; the key and the toolchain name are what
    /// the wizard shows.
    /// </remarks>
    [Fact]
    public async Task InstallAsync_WhenLauncherMissingFromPath_ShouldCarryLocalizableToolchainAdvice()
    {
        var probe = new StubAcpExecutableProbe();
        probe.SetResolvedPath("npm", null);
        var installer = new DesktopAcpComponentInstaller(probe);

        var result = await installer.InstallAsync(AcpSetupFixtures.NpxComponent(), onOutput: null, overrides: null, TestContext.Current.CancellationToken);

        Assert.Equal(
            DesktopAcpComponentInstaller.ToolchainMissingRemediationKey,
            result.RemediationKey);
        Assert.Equal("Node.js", result.MissingToolchainName);
    }

    /// <summary>
    /// Reverse verification: a failure from the package manager itself carries no toolchain advice, so the
    /// key marks the one cause it names rather than every install failure.
    /// </summary>
    [Fact]
    public async Task InstallAsync_ForBinaryDistribution_ShouldCarryNoToolchainAdvice()
    {
        var installer = new DesktopAcpComponentInstaller(new StubAcpExecutableProbe());

        var result = await installer.InstallAsync(AcpSetupFixtures.BinaryComponent(), onOutput: null, overrides: null, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(result.RemediationKey);
        Assert.Null(result.MissingToolchainName);
    }

    /// <summary>
    /// The component's own launcher is resolved before the manager, because the manager is derived from
    /// the launcher's directory: a user who names <c>npx</c> has named which toolchain's <c>npm</c> to
    /// install through, and deriving that needs the launcher's real path.
    /// </summary>
    [Fact]
    public async Task InstallAsync_ForNpxDistribution_ShouldResolveTheLauncherThenNpm()
    {
        var probe = new StubAcpExecutableProbe();
        probe.SetResolvedPath("npm", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var installer = new DesktopAcpComponentInstaller(probe);

        var result = await installer.InstallAsync(AcpSetupFixtures.NpxComponent(), onOutput: null, overrides: null, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "npx", "npm" }, probe.ResolveRequests);
        // The resolved path does not exist, so the attempt fails at start rather than reporting success.
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task InstallAsync_ForUvxDistribution_ShouldResolveTheLauncherThenUv()
    {
        var probe = new StubAcpExecutableProbe();
        probe.SetResolvedPath("uv", null);
        var installer = new DesktopAcpComponentInstaller(probe);

        var result = await installer.InstallAsync(AcpSetupFixtures.UvxComponent(), onOutput: null, overrides: null, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "uvx", "uv" }, probe.ResolveRequests);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// The install must run through the toolchain the user named, not through whatever bare <c>npm</c>
    /// the inherited PATH resolves.
    /// </summary>
    /// <remarks>
    /// Installing into the wrong toolchain is worse than failing: npm reports success, and the component
    /// lands where the next probe and the saved profile will never look — so the wizard advances on a
    /// success that guarantees a launch failure. A GUI process is exactly where the two diverge, since it
    /// cannot see the PATH a shell profile builds.
    /// </remarks>
    [Fact]
    public async Task InstallAsync_WithOverriddenLauncher_ShouldInstallThroughThatToolchainsNpm()
    {
        const string toolchainBin = "/opt/toolchain/node/bin";
        var probe = new StubAcpExecutableProbe();
        probe.SetResolvedPath(toolchainBin + "/npx", toolchainBin + "/npx");
        probe.SetResolvedPath(toolchainBin + "/npm", toolchainBin + "/npm");
        var installer = new DesktopAcpComponentInstaller(probe);
        var overrides = AcpCommandOverrides.Create(new Dictionary<string, string>
        {
            ["npx"] = toolchainBin + "/npx"
        });

        await installer.InstallAsync(AcpSetupFixtures.NpxComponent(), onOutput: null, overrides, TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { toolchainBin + "/npx", toolchainBin + "/npm" },
            probe.ResolveRequests);
    }

    /// <summary>
    /// An explicit manager override wins over the sibling derivation, since the derivation exists to
    /// spare the user a second answer rather than to overrule one they gave.
    /// </summary>
    [Fact]
    public async Task InstallAsync_WithExplicitManagerOverride_ShouldPreferItOverTheSibling()
    {
        var probe = new StubAcpExecutableProbe();
        probe.SetResolvedPath("/opt/node/bin/npx", "/opt/node/bin/npx");
        probe.SetResolvedPath("/elsewhere/bin/npm", "/elsewhere/bin/npm");
        var installer = new DesktopAcpComponentInstaller(probe);
        var overrides = AcpCommandOverrides.Create(new Dictionary<string, string>
        {
            ["npx"] = "/opt/node/bin/npx",
            ["npm"] = "/elsewhere/bin/npm"
        });

        await installer.InstallAsync(AcpSetupFixtures.NpxComponent(), onOutput: null, overrides, TestContext.Current.CancellationToken);

        Assert.Contains("/elsewhere/bin/npm", probe.ResolveRequests);
        Assert.DoesNotContain("/opt/node/bin/npm", probe.ResolveRequests);
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

        var result = await installer.InstallAsync(component, onOutput: null, overrides: null, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(probe.ResolveRequests);
    }

    [Fact]
    public void SupportsAutomaticInstall_OnDesktop_ShouldBeTrue()
        => Assert.True(new DesktopAcpComponentInstaller(new StubAcpExecutableProbe()).SupportsAutomaticInstall);
}
