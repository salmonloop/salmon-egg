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
    private readonly IAcpSetupConnectivityTester _connectivityTester;
    private readonly IConfigurationService _configurationService;

    public AcpSetupWizardOrchestrator(
        IAcpAgentCatalog catalog,
        IAcpExecutableProbe probe,
        IAcpComponentInstaller installer,
        IAcpSetupConnectivityTester connectivityTester,
        IConfigurationService configurationService)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _detector = new AcpComponentDetector(probe ?? throw new ArgumentNullException(nameof(probe)));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _connectivityTester = connectivityTester ?? throw new ArgumentNullException(nameof(connectivityTester));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
    }

    /// <summary>True when this platform can install components for the user.</summary>
    public bool SupportsAutomaticInstall => _installer.SupportsAutomaticInstall;

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
            states.Add(new AcpAgentDetectionState { Agent = agent, Runtime = runtime });
        }

        return states;
    }

    public Task<AcpComponentProbeResult> DetectComponentAsync(
        AcpComponentDescriptor component,
        AcpCommandOverrides? overrides = null,
        CancellationToken cancellationToken = default)
        => _detector.DetectAsync(component, overrides, cancellationToken);

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
        var probe = await _detector
            .DetectAsync(component, overrides, cancellationToken)
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
            StdioEnvironment = new Dictionary<string, string>(launchPlan.Environment, StringComparer.Ordinal)
        };
    }
}
