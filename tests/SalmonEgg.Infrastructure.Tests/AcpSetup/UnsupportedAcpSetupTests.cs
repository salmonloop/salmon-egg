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

        Assert.Null(await probe.ResolveExecutablePathAsync("npx"));
        Assert.Null(await probe.ReadVersionAsync("npx", new[] { "--version" }));
        Assert.Null((await probe.LocateGlobalNodePackageAsync("@scope/pkg")).IsInstalled);
        Assert.Null((await probe.LocateGlobalUvToolAsync("tool")).IsInstalled);
    }

    [Fact]
    public void Installer_ShouldReportAutomaticInstallUnsupported()
        => Assert.False(new UnsupportedAcpComponentInstaller().SupportsAutomaticInstall);

    [Fact]
    public async Task Installer_WhenCalledAnyway_ShouldFailInsteadOfReportingSuccess()
    {
        var component = AcpSetupFixtures.NpxComponent();

        var result = await new UnsupportedAcpComponentInstaller().InstallAsync(component);

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
            .TestAsync(AcpSetupFixtures.Plan("npx", "@scope/adapter"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AcpSetupTestStage.CommandResolution, result.Stage);
        Assert.Contains("not supported", result.ErrorDetail);
    }
}
