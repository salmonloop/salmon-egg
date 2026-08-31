using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Infrastructure.AcpSetup;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// Guards the degradation contract for platforms with no child-process host: every seam must report
/// "cannot answer" or "cannot do", and never a value the wizard would mistake for a real finding.
/// </summary>
public sealed class UnsupportedAcpSetupTests
{
    [Fact]
    public void Probe_ShouldReportProcessProbingUnsupported()
        => Assert.False(new UnsupportedAcpExecutableProbe().SupportsProcessProbing);

    /// <summary>
    /// Null, not false: a false here would make the wizard tell the user to install a component it
    /// never looked for.
    /// </summary>
    [Fact]
    public async Task Probe_ShouldAnswerEveryQueryAsUnknown()
    {
        var probe = new UnsupportedAcpExecutableProbe();

        Assert.Null(await probe.ResolveExecutablePathAsync("npx", TestContext.Current.CancellationToken));
        Assert.Null(await probe.ReadVersionAsync("npx", new[] { "--version" }, TestContext.Current.CancellationToken));
        Assert.Null((await probe.LocateGlobalPackageAsync(
            AcpDistributionKind.Npx,
            "@scope/pkg",
            AcpPackageManagerCandidates.Exact("npm"),
            TestContext.Current.CancellationToken)).IsInstalled);
        Assert.Null((await probe.LocateGlobalPackageAsync(
            AcpDistributionKind.Uvx,
            "tool",
            AcpPackageManagerCandidates.Exact("uv"),
            TestContext.Current.CancellationToken)).IsInstalled);
    }

    [Fact]
    public void Installer_ShouldReportAutomaticInstallUnsupported()
        => Assert.False(new UnsupportedAcpComponentInstaller().SupportsAutomaticInstall);

    [Fact]
    public async Task Installer_WhenCalledAnyway_ShouldFailInsteadOfReportingSuccess()
    {
        var component = AcpSetupFixtures.NpxComponent();

        var result = await new UnsupportedAcpComponentInstaller()
            .InstallAsync(component, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(component.Id, result.ComponentId);
        Assert.Null(result.ExitCode);
        Assert.Contains("not supported", result.ErrorDetail);
    }

    /// <summary>
    /// Fails at command resolution rather than validation: the plan itself is well-formed, it is this
    /// platform that cannot run it, and a green test would let the wizard save an unusable profile.
    /// </summary>
    [Fact]
    public async Task ConnectivityTester_ShouldFailAtCommandResolution()
    {
        var result = await new UnsupportedAcpSetupConnectivityTester()
            .TestAsync(AcpSetupFixtures.Plan("npx", "@scope/adapter"), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AcpSetupTestStage.CommandResolution, result.Stage);
        Assert.Contains("not supported", result.ErrorDetail);
    }
}
