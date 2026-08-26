using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Application.Services.AcpSetup;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;
using Xunit;

namespace SalmonEgg.Application.Tests.Services.AcpSetup;

/// <summary>
/// Guards that a user-supplied launcher path also decides which package manager answers the detection
/// query.
/// </summary>
/// <remarks>
/// A GUI process cannot see a version-manager toolchain, so the wizard lets the user name the launcher
/// their adapter runs through. Detecting that adapter then asks a package manager whether the package is
/// installed — and asking the <em>wrong</em> toolchain's manager answers about the wrong machine state:
/// a package installed under the named toolchain reads as absent, or worse, absent under the named one
/// reads as installed. Either way the wizard acts on a fact about a toolchain the user did not choose.
///
/// The manager is reachable without asking the user for a second path because it is the launcher's
/// sibling: npm publishes <c>npm</c> and <c>npx</c> from one package into one bin directory, and the uv
/// installer lays down <c>uv</c> and <c>uvx</c> side by side. Naming one names the other.
/// </remarks>
public sealed class AcpPackageManagerOverrideTests
{
    private const string NodeToolchainBin = "/opt/toolchain/node/bin";

    private static AcpComponentDescriptor NpxAdapter() => new()
    {
        Id = "adapter",
        DisplayName = "Adapter",
        Distribution = AcpDistributionKind.Npx,
        DetectionMode = AcpComponentDetectionMode.GlobalNodePackage,
        ProbeCommand = "npx",
        PackageId = "@scope/adapter"
    };

    private static AcpComponentDescriptor UvxAdapter() => new()
    {
        Id = "adapter",
        DisplayName = "Adapter",
        Distribution = AcpDistributionKind.Uvx,
        DetectionMode = AcpComponentDetectionMode.GlobalUvTool,
        ProbeCommand = "uvx",
        PackageId = "some-tool"
    };

    /// <summary>
    /// The launcher the user named decides the toolchain, so the manager queried must be the sibling of
    /// that launcher rather than whatever bare <c>npm</c> resolves to on the inherited PATH.
    /// </summary>
    [Fact]
    public async Task DetectAsync_WithOverriddenNpxLauncher_ShouldQueryThatToolchainsNpm()
    {
        var probe = new RecordingProbe();
        var overrides = AcpCommandOverrides.Create(new Dictionary<string, string>
        {
            ["npx"] = NodeToolchainBin + "/npx"
        });

        await new AcpComponentDetector(probe).DetectAsync(
            NpxAdapter(),
            overrides,
            TestContext.Current.CancellationToken);

        Assert.Equal(NodeToolchainBin + "/npm", probe.QueriedPackageManager);
    }

    /// <summary>The same contract for the Python toolchain: <c>uvx</c> names <c>uv</c>.</summary>
    [Fact]
    public async Task DetectAsync_WithOverriddenUvxLauncher_ShouldQueryThatToolchainsUv()
    {
        var probe = new RecordingProbe();
        var overrides = AcpCommandOverrides.Create(new Dictionary<string, string>
        {
            ["uvx"] = "/opt/toolchain/python/bin/uvx"
        });

        await new AcpComponentDetector(probe).DetectAsync(
            UvxAdapter(),
            overrides,
            TestContext.Current.CancellationToken);

        Assert.Equal("/opt/toolchain/python/bin/uv", probe.QueriedPackageManager);
    }

    /// <summary>
    /// With no override the manager stays a bare name, so PATH resolution answers exactly as before.
    /// </summary>
    [Fact]
    public async Task DetectAsync_WithoutOverride_ShouldQueryTheBareManagerName()
    {
        var probe = new RecordingProbe();

        await new AcpComponentDetector(probe).DetectAsync(
            NpxAdapter(),
            overrides: null,
            TestContext.Current.CancellationToken);

        Assert.Equal("npm", probe.QueriedPackageManager);
    }

    /// <summary>
    /// An explicit manager override wins over the sibling derivation: the derivation exists to spare the
    /// user a second answer, not to overrule one they gave.
    /// </summary>
    [Fact]
    public async Task DetectAsync_WithExplicitManagerOverride_ShouldPreferItOverTheSibling()
    {
        var probe = new RecordingProbe();
        var overrides = AcpCommandOverrides.Create(new Dictionary<string, string>
        {
            ["npx"] = NodeToolchainBin + "/npx",
            ["npm"] = "/elsewhere/bin/npm"
        });

        await new AcpComponentDetector(probe).DetectAsync(
            NpxAdapter(),
            overrides,
            TestContext.Current.CancellationToken);

        Assert.Equal("/elsewhere/bin/npm", probe.QueriedPackageManager);
    }

    /// <summary>
    /// Records which package manager the detector asked, so the assertion is about the toolchain chosen
    /// rather than about a query result the stub invented.
    /// </summary>
    private sealed class RecordingProbe : IAcpExecutableProbe
    {
        public string? QueriedPackageManager { get; private set; }

        public bool SupportsProcessProbing => true;

        public Task<string?> ResolveExecutablePathAsync(
            string command,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(command);

        public Task<IReadOnlyList<string>> ResolveExecutableCandidatesAsync(
            string command,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { command });

        public Task<string?> ReadVersionAsync(
            string command,
            IReadOnlyList<string> versionArguments,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<AcpPackageQueryResult> LocateGlobalPackageAsync(
            AcpDistributionKind distribution,
            string packageId,
            AcpPackageManagerCandidates packageManager,
            CancellationToken cancellationToken = default)
        {
            QueriedPackageManager = packageManager.Preferred;
            return Task.FromResult(AcpPackageQueryResult.Unknown(packageManager.Preferred));
        }
    }
}
