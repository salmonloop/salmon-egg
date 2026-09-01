using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Application.Services.AcpSetup;

/// <summary>
/// Drives the ACP setup wizard: detects catalog agents and their adapters, installs missing components,
/// tests a launch plan, and persists the result as a connection profile. Owns no UI state — the
/// presentation layer keeps the step machine and calls into this for every side effect.
/// </summary>
public sealed class AcpSetupWizardOrchestrator
{
    private readonly IAcpAgentCatalog _catalog;
    private readonly AcpComponentDetector _detector;
    private readonly IAcpComponentInstaller _installer;
    private readonly IAcpToolchainInstaller? _toolchainInstaller;
    private readonly IAcpSetupConnectivityTester _connectivityTester;
    private readonly IConfigurationService _configurationService;

    /// <param name="toolchainInstaller">
    /// Installs a missing toolchain. Optional so a caller that only detects and tests need not supply one;
    /// null reports <see cref="SupportsToolchainInstall"/> as false, which is the same answer a platform
    /// without an installer gives, so the wizard has one condition to check rather than two.
    /// </param>
    public AcpSetupWizardOrchestrator(
        IAcpAgentCatalog catalog,
        IAcpExecutableProbe probe,
        IAcpComponentInstaller installer,
        IAcpSetupConnectivityTester connectivityTester,
        IConfigurationService configurationService,
        IAcpToolchainInstaller? toolchainInstaller = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _detector = new AcpComponentDetector(probe ?? throw new ArgumentNullException(nameof(probe)));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _connectivityTester = connectivityTester ?? throw new ArgumentNullException(nameof(connectivityTester));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _toolchainInstaller = toolchainInstaller;
    }

    /// <summary>
    /// True when this platform can run installers at all.
    /// </summary>
    /// <remarks>
    /// A platform capability. Whether <em>this machine</em> has the toolchain an install needs is
    /// <see cref="DetectToolchainAsync"/>'s answer, and a caller offering a one-click install needs both.
    /// </remarks>
    public bool SupportsAutomaticInstall => _installer.SupportsAutomaticInstall;

    /// <summary>
    /// True when this platform can install a missing toolchain itself.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="SupportsAutomaticInstall"/>, which is about running a package manager that
    /// already exists. The two are independent: a platform can run npm and still have no way to obtain Node.
    /// Whether a <em>particular</em> toolchain has a published source is
    /// <see cref="AcpToolchainRequirement.HasAutomaticInstallPath"/>'s answer.
    /// </remarks>
    public bool SupportsToolchainInstall => _toolchainInstaller?.SupportsAutomaticInstall == true;

    public IReadOnlyList<AcpAgentDescriptor> Agents => _catalog.Agents;

    /// <summary>
    /// Probes every catalog agent's runtime. Adapters are not probed here: probing every adapter up front
    /// would multiply process launches for agents the user will never pick.
    /// </summary>
    public async Task<IReadOnlyList<AcpAgentDetectionState>> DetectAgentsAsync(
        AcpCommandOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        var states = new List<AcpAgentDetectionState>(_catalog.Agents.Count);
        foreach (var agent in _catalog.Agents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtime = await _detector
                .DetectAsync(agent.Runtime, overrides, cancellationToken)
                .ConfigureAwait(false);

            // Probed alongside the runtime rather than on demand from the row, so a row that has to
            // decide whether to offer an install already holds the answer. Deferring it would either
            // show a button before the answer arrives — the defect this replaces — or make every row
            // launch its own process after the sweep already had the chance.
            var toolchain = await _detector
                .DetectToolchainAsync(agent.Runtime, overrides, cancellationToken)
                .ConfigureAwait(false);

            states.Add(new AcpAgentDetectionState
            {
                Agent = agent,
                Runtime = runtime,
                RuntimeToolchain = toolchain
            });
        }

        return states;
    }

    public Task<AcpComponentProbeResult> DetectComponentAsync(
        AcpComponentDescriptor component,
        AcpCommandOverrides? overrides = null,
        CancellationToken cancellationToken = default)
        => _detector.DetectAsync(component, overrides, cancellationToken);

    /// <summary>
    /// Probes the toolchain <paramref name="component"/> installs through, or returns null when it needs
    /// none. Callers offer a one-click install only when the result allows the attempt.
    /// </summary>
    public Task<AcpToolchainProbeResult?> DetectToolchainAsync(
        AcpComponentDescriptor component,
        AcpCommandOverrides? overrides = null,
        CancellationToken cancellationToken = default)
        => _detector.DetectToolchainAsync(component, overrides, cancellationToken);

    /// <summary>
    /// Makes the next detection search the machine again instead of answering from a cached search.
    /// </summary>
    /// <remarks>
    /// For callers re-detecting because the user asked. A search is cached to keep the wizard from spawning
    /// a login shell per component, which means the answer outlives whatever the user did in between — and
    /// what the wizard asks them to do is install the missing toolchain and detect again.
    /// </remarks>
    public void InvalidateSearchPaths() => _detector.InvalidateSearchPaths();

    /// <summary>
    /// Installs a component and re-probes it, so callers always see verified availability rather than
    /// trusting the installer's exit code.
    /// </summary>
    public async Task<(AcpComponentInstallResult Install, AcpComponentProbeResult Probe)> InstallComponentAsync(
        AcpComponentDescriptor component,
        Action<string>? onOutput = null,
        AcpCommandOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        // The same overrides govern the install and the re-probe, so the install lands in the toolchain
        // the probe then asks about.
        var install = await _installer
            .InstallAsync(component, onOutput, overrides, cancellationToken)
            .ConfigureAwait(false);

        // An install is the one moment this layer knows the machine changed, so the re-probe below must not
        // reuse the search that was current before it. A package manager places a new executable in its own
        // bin directory, and a first install through a manager can create that directory outright — which a
        // cached directory list cannot contain.
        _detector.InvalidateSearchPaths();

        var probe = await _detector
            .DetectAsync(component, overrides, cancellationToken)
            .ConfigureAwait(false);
        return (install, probe);
    }

    /// <summary>
    /// Installs the toolchain a component needs and re-probes that toolchain, so callers see verified
    /// availability rather than trusting a downloader's success report.
    /// </summary>
    /// <remarks>
    /// The install is requested in terms of a component because that is what the wizard has selected; the
    /// component determines whether it needs Node at all. A component with no toolchain is a programming
    /// error here — the UI must not surface a toolchain button for it — so it fails clearly rather than
    /// pretending an empty install succeeded.
    /// </remarks>
    public async Task<(AcpToolchainInstallResult Install, AcpToolchainProbeResult? Probe)> InstallToolchainAsync(
        AcpComponentDescriptor component,
        Action<string>? onOutput = null,
        AcpCommandOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component.RequiredToolchain is not { } requirement)
        {
            throw new InvalidOperationException(
                $"Component '{component.Id}' does not declare a required toolchain.");
        }

        var installer = _toolchainInstaller;
        if (installer is null)
        {
            return (
                AcpToolchainInstallResult.Failure(
                    requirement,
                    "Automatic toolchain installation is unavailable on this platform."),
                await _detector
                    .DetectToolchainAsync(component, overrides, cancellationToken)
                    .ConfigureAwait(false));
        }

        var install = await installer
            .InstallAsync(requirement, onOutput, cancellationToken)
            .ConfigureAwait(false);

        // This must sit between install and probe. A first toolchain install creates an entire version/bin
        // directory that did not exist when the detector cached its search paths; asking the detector again
        // without invalidating gives a stale "missing" even though the installer just succeeded. The
        // accompanying test drives an installer that creates that directory and reverse-verifies that
        // removing this one call makes the outcome red.
        _detector.InvalidateSearchPaths();

        var probe = await _detector
            .DetectToolchainAsync(component, overrides, cancellationToken)
            .ConfigureAwait(false);
        return (install, probe);
    }

    /// <summary>
    /// Validates the draft's parameters and, when they pass, tests the launch plan end to end.
    /// Validation failures short-circuit as a <see cref="AcpSetupTestStage.Validation"/> result so the
    /// caller has one uniform shape to render.
    /// </summary>
    public async Task<AcpSetupTestResult> TestDraftAsync(
        AcpSetupDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var violations = AcpSetupParameterValidator.Validate(
            draft.Adapter.LaunchTemplate,
            draft.ParameterValues);
        if (violations.Count > 0)
        {
            return AcpSetupTestResult.Failure(
                AcpSetupTestStage.Validation,
                errorDetail: null,
                remediationKey: violations[0].MessageKey);
        }

        return await _connectivityTester
            .TestAsync(draft.BuildLaunchPlan(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Persists the draft as a new stdio connection profile and returns it. The caller is expected to have
    /// tested the draft first; this method does not re-test, so a caller that skips testing saves an
    /// unverified configuration deliberately rather than by accident.
    /// </summary>
    public async Task<ServerConfiguration> SaveDraftAsync(
        AcpSetupDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        var configuration = CreateConfiguration(draft);
        await _configurationService.SaveConfigurationAsync(configuration).ConfigureAwait(false);
        return configuration;
    }

    internal static ServerConfiguration CreateConfiguration(AcpSetupDraft draft)
    {
        var launchPlan = draft.BuildLaunchPlan();
        return new ServerConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = draft.ProfileName,
            Transport = TransportType.Stdio,
            StdioCommand = launchPlan.Command,
            StdioArguments = new List<string>(launchPlan.Arguments),
            StdioEnvironment = new Dictionary<string, string>(launchPlan.Environment, StringComparer.Ordinal),
            Verification = draft.Verification
        };
    }
}
