using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Application.Services.AcpSetup;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;
using Xunit;

namespace SalmonEgg.Application.Tests.Services.AcpSetup;

/// <summary>
/// Guards that the wizard establishes whether the toolchain an install runs through exists, before it
/// offers that install.
/// </summary>
/// <remarks>
/// The wizard shipped answering "can this be installed" from the catalog alone: a Node package with a
/// package id reported itself installable on a machine with no Node at all, so the absence surfaced only
/// as a failed install after the user clicked. This probe is what turns that into a fact known up front.
///
/// Its whole value rests on predicting the installer faithfully. The install derives its package manager
/// from the launcher's own directory and uses only the preferred candidate, so this must derive the same
/// command from the same inputs — a probe that looked somewhere else would either promise an install that
/// fails or withhold one that would have worked.
/// </remarks>
public sealed class AcpToolchainDetectionTests
{
    private const string NodeToolchainBin = "/opt/toolchain/node/bin";

    private static AcpComponentDescriptor NpxRuntime() => new()
    {
        Id = "runtime",
        DisplayName = "Agent CLI",
        Distribution = AcpDistributionKind.Npx,
        DetectionMode = AcpComponentDetectionMode.ExecutableOnPath,
        ProbeCommand = "agent",
        PackageId = "@scope/agent"
    };

    private static AcpComponentDescriptor UvxRuntime() => new()
    {
        Id = "runtime",
        DisplayName = "Agent CLI",
        Distribution = AcpDistributionKind.Uvx,
        DetectionMode = AcpComponentDetectionMode.ExecutableOnPath,
        ProbeCommand = "agent",
        PackageId = "some-tool"
    };

    private static AcpComponentDescriptor BinaryRuntime() => new()
    {
        Id = "runtime",
        DisplayName = "Agent CLI",
        Distribution = AcpDistributionKind.Binary,
        DetectionMode = AcpComponentDetectionMode.ExecutableOnPath,
        ProbeCommand = "agent",
        InstallDocumentation = new Uri("https://example.invalid/install")
    };

    private static AcpComponentDescriptor BuiltInAdapter() => new()
    {
        Id = "adapter",
        DisplayName = "Built-in ACP",
        Distribution = AcpDistributionKind.BuiltIn,
        DetectionMode = AcpComponentDetectionMode.None
    };

    /// <summary>
    /// The defect's root: a Node package on a machine with no npm. The probe must report the toolchain
    /// missing and name it, so the wizard can withdraw the offer and say why.
    /// </summary>
    [Fact]
    public async Task DetectToolchainAsync_WithNoPackageManagerOnPath_ReportsMissingAndNamesTheToolchain()
    {
        var probe = new StubProbe();

        var result = await new AcpComponentDetector(probe).DetectToolchainAsync(
            NpxRuntime(),
            overrides: null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result!.IsMissing);
        Assert.False(result.AllowsInstallAttempt);
        Assert.Equal("Node.js", result.Requirement.DisplayName);
        // The advice has to lead somewhere, or naming the toolchain is a dead end.
        Assert.NotNull(result.Requirement.Documentation);
    }

    /// <summary>
    /// Reverse verification: with npm present the same component must read as available, so the check
    /// narrows the offer for a real absence rather than suppressing it everywhere.
    /// </summary>
    [Fact]
    public async Task DetectToolchainAsync_WithPackageManagerOnPath_ReportsAvailable()
    {
        var probe = new StubProbe();
        probe.SetExecutable("npm", "/usr/bin/npm");

        var result = await new AcpComponentDetector(probe).DetectToolchainAsync(
            NpxRuntime(),
            overrides: null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(AcpToolchainAvailability.Available, result!.Availability);
        Assert.True(result.AllowsInstallAttempt);
        Assert.Equal("/usr/bin/npm", result.ManagerPath);
    }

    /// <summary>
    /// The prediction contract. The installer resolves the component's launcher, derives the manager as
    /// that launcher's sibling, and uses only the preferred candidate. So a machine whose npm sits beside
    /// the resolved launcher is installable, and this must be the command the probe looks for — asking a
    /// bare <c>npm</c> instead would answer about a different toolchain.
    /// </summary>
    [Fact]
    public async Task DetectToolchainAsync_DerivesTheManagerAsTheResolvedLaunchersSibling()
    {
        var probe = new StubProbe();
        probe.SetExecutable("agent", NodeToolchainBin + "/agent");
        probe.SetExecutable(NodeToolchainBin + "/npm", NodeToolchainBin + "/npm");

        var result = await new AcpComponentDetector(probe).DetectToolchainAsync(
            NpxRuntime(),
            overrides: null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(AcpToolchainAvailability.Available, result!.Availability);
        Assert.Equal(NodeToolchainBin + "/npm", result.ManagerPath);
        Assert.Contains(NodeToolchainBin + "/npm", probe.ResolvedCommands);
    }

    /// <summary>
    /// A user-named launcher decides the toolchain here exactly as it does for detection and installation,
    /// so the answer is about the toolchain they chose.
    /// </summary>
    [Fact]
    public async Task DetectToolchainAsync_HonoursALauncherOverride()
    {
        var probe = new StubProbe();
        probe.SetExecutable(NodeToolchainBin + "/agent", NodeToolchainBin + "/agent");
        probe.SetExecutable(NodeToolchainBin + "/npm", NodeToolchainBin + "/npm");
        var overrides = AcpCommandOverrides.Create(new Dictionary<string, string>
        {
            ["agent"] = NodeToolchainBin + "/agent"
        });

        var result = await new AcpComponentDetector(probe).DetectToolchainAsync(
            NpxRuntime(),
            overrides,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(NodeToolchainBin + "/npm", result!.ManagerPath);
    }

    /// <summary>
    /// A launcher that does not resolve is ordinary rather than fatal: the manager may be on PATH while the
    /// component is not, which is precisely the state an install is meant to fix.
    /// </summary>
    [Fact]
    public async Task DetectToolchainAsync_WithUnresolvedLauncher_StillFindsAManagerOnPath()
    {
        var probe = new StubProbe();
        probe.SetExecutable("npm", "/usr/bin/npm");

        var result = await new AcpComponentDetector(probe).DetectToolchainAsync(
            NpxRuntime(),
            overrides: null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(AcpToolchainAvailability.Available, result!.Availability);
    }

    /// <summary>The Python toolchain is modelled the same way, so the concept is not Node-specific.</summary>
    [Fact]
    public async Task DetectToolchainAsync_ForUvxDistribution_NamesTheUvToolchain()
    {
        var probe = new StubProbe();

        var result = await new AcpComponentDetector(probe).DetectToolchainAsync(
            UvxRuntime(),
            overrides: null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result!.IsMissing);
        Assert.Equal("uv", result.Requirement.DisplayName);
    }

    /// <summary>
    /// Distributions the wizard never installs have no prerequisite to report. Null rather than "missing":
    /// a binary distribution is not blocked by the absence of a package manager it would never use, and
    /// reporting one would tell most of the catalog's users to install a toolchain for nothing.
    /// </summary>
    [Theory]
    [InlineData("binary")]
    [InlineData("builtin")]
    public async Task DetectToolchainAsync_ForDistributionsWithNoInstallPath_ReportsNoRequirement(string kind)
    {
        var component = kind == "binary" ? BinaryRuntime() : BuiltInAdapter();

        var result = await new AcpComponentDetector(new StubProbe()).DetectToolchainAsync(
            component,
            overrides: null,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    /// <summary>
    /// A platform that cannot start processes has not established absence, so the verdict is undetermined
    /// and the install attempt stays allowed — matching how an undetermined component probe does not block
    /// the wizard. Reporting "missing" here would send a WASM user to install Node for no reason.
    /// </summary>
    [Fact]
    public async Task DetectToolchainAsync_WhenProbingIsUnsupported_IsUndeterminedAndStillAllowsTheAttempt()
    {
        var probe = new StubProbe { SupportsProcessProbing = false };

        var result = await new AcpComponentDetector(probe).DetectToolchainAsync(
            NpxRuntime(),
            overrides: null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(AcpToolchainAvailability.Undetermined, result!.Availability);
        Assert.False(result.IsMissing);
        Assert.True(result.AllowsInstallAttempt);
    }

    /// <summary>
    /// Probe answering from declared paths and recording what was asked for, so an assertion can be about
    /// the command the detector chose rather than only about the verdict it returned.
    /// </summary>
    private sealed class StubProbe : IAcpExecutableProbe
    {
        private readonly Dictionary<string, string?> _paths = new(StringComparer.Ordinal);

        public bool SupportsProcessProbing { get; set; } = true;

        /// <summary>How many times a caller asked for the search to be redone.</summary>
        public int InvalidateCount { get; private set; }

        public void InvalidateSearchPaths() => InvalidateCount++;

        public List<string> ResolvedCommands { get; } = new();

        public void SetExecutable(string command, string? path) => _paths[command] = path;

        public Task<string?> ResolveExecutablePathAsync(
            string command,
            CancellationToken cancellationToken = default)
        {
            ResolvedCommands.Add(command);
            return Task.FromResult(_paths.TryGetValue(command, out var path) ? path : null);
        }

        public Task<IReadOnlyList<string>> ResolveExecutableCandidatesAsync(
            string command,
            CancellationToken cancellationToken = default)
        {
            var resolved = _paths.TryGetValue(command, out var path) ? path : null;
            return Task.FromResult<IReadOnlyList<string>>(
                resolved is null ? Array.Empty<string>() : new[] { resolved });
        }

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
            => Task.FromResult(AcpPackageQueryResult.Unknown(packageManager.Preferred));
    }
}
