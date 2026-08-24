using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using SalmonEgg.Application.Services.AcpSetup;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings.AcpSetup;

public sealed class AcpSetupWizardViewModelTests
{
    private const string HandshakeRemediationKey = "AcpSetup_Remediation_Handshake";
    private const string StageHandshakeKey = "AcpSetup_Stage_Handshake";
    private const string AgentDescriptionKey = "AcpSetup_Agent_Test_Description";

    [Fact]
    public void Constructor_SeedsCatalogRows_AsUndeterminedOnFirstStep()
    {
        var wizard = CreateWizard();

        Assert.Single(wizard.Agents);
        Assert.Equal(AcpComponentAvailability.Undetermined, wizard.Agents[0].Availability);
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
        Assert.True(wizard.IsOnAgentSelection);
        Assert.False(wizard.GoBackCommand.CanExecute(null));
        Assert.False(wizard.GoNextCommand.CanExecute(null));
    }

    /// <summary>
    /// The catalog carries a resource key for each agent description, so the row must resolve it. A
    /// view bound straight to the key renders the key — which is what shipped, and what no test caught
    /// because nothing asserted on the text a user reads.
    /// </summary>
    [Fact]
    public void AgentRow_ResolvesDescriptionKey_AgainstCoreStrings()
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", AgentDescriptionKey, "\u7528\u4e8e\u6d4b\u8bd5\u7684 Agent\u3002");
        var wizard = CreateWizard(localizer: localizer);

        var row = Assert.Single(wizard.Agents);
        Assert.Equal("\u7528\u4e8e\u6d4b\u8bd5\u7684 Agent\u3002", row.Description);
        // The key itself must not survive to the surface the view binds.
        Assert.NotEqual(AgentDescriptionKey, row.Description);
    }

    /// <summary>
    /// With no localizer the row falls back to the key rather than an empty string: a blank caption is
    /// indistinguishable from a layout bug, while the key is diagnosable.
    /// </summary>
    [Fact]
    public void AgentRow_WithoutLocalizer_FallsBackToKey_NotEmpty()
    {
        var wizard = CreateWizard();

        var row = Assert.Single(wizard.Agents);
        Assert.Equal(AgentDescriptionKey, row.Description);
    }

    [Fact]
    public async Task ParameterRow_ResolvesDescriptionKey_AgainstCoreStrings()
    {
        const string descriptionKey = "AcpSetup_Parameter_Model_Description";
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", descriptionKey, "\u6a21\u578b\u540d\u79f0\u3002");
        var parameter = AcpSetupWizardFixtures.Parameter("--model", description: descriptionKey);
        var (wizard, _, _) = await WalkToParametersAsync(new[] { parameter }, localizer);

        var row = Assert.Single(wizard.Parameters);
        Assert.Equal("\u6a21\u578b\u540d\u79f0\u3002", row.Description);
        Assert.NotEqual(descriptionKey, row.Description);
    }

    /// <summary>
    /// The install surface is bound to properties computed from <c>InstallOutput</c>, and a collection's
    /// own change notifications say nothing about properties derived from it. The wizard shipped without
    /// raising them, so <c>HasInstallOutput</c> stayed false forever and the output panel never appeared —
    /// the only progress feedback an install has. This asserts the notifications, not just the values.
    /// </summary>
    [Fact]
    public async Task InstallAgentRow_StreamsOutput_AndNotifiesTheBoundSurface()
    {
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, path: null);
        var installer = new StubComponentInstaller();
        installer.OutputLines.Add("added 1 package");
        installer.OutputLines.Add("done in 2s");
        var wizard = CreateWizard(probe, installer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = Assert.Single(wizard.Agents);
        Assert.True(row.IsMissing);

        var changed = new List<string>();
        wizard.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);

        // Installing flips the probe's answer so the post-install re-probe reports the runtime present.
        installer.OnInstall += _ =>
            probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.0.0");
        row.RequestInstall();
        // The install runs to completion inline here: the stub installer and the test dispatcher are
        // both synchronous, so a settled busy flag is proof the whole chain finished rather than a race.
        Assert.False(wizard.IsBusy);

        Assert.Equal(new[] { "added 1 package", "done in 2s" }, wizard.InstallOutput);
        Assert.True(wizard.HasInstallOutput);
        Assert.Equal("done in 2s", wizard.LatestInstallOutputLine);
        // Without these the bound Visibility and Text never update, which is the shipped defect.
        Assert.Contains(nameof(wizard.HasInstallOutput), changed);
        Assert.Contains(nameof(wizard.LatestInstallOutputLine), changed);
    }

    /// <summary>
    /// Moving to a new component clears the previous install's log, and that clear must notify too or
    /// the panel keeps showing output belonging to a component the user is no longer setting up.
    /// </summary>
    [Fact]
    public async Task AdvancingToComponentSetup_ClearsInstallOutput_AndNotifies()
    {
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, path: null);
        var installer = new StubComponentInstaller();
        installer.OutputLines.Add("added 1 package");
        var wizard = CreateWizard(probe, installer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = Assert.Single(wizard.Agents);
        installer.OnInstall += _ =>
            probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.0.0");
        row.RequestInstall();
        Assert.False(wizard.IsBusy);
        Assert.True(wizard.HasInstallOutput);

        var changed = new List<string>();
        wizard.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);
        wizard.SelectedAgent = row;
        await wizard.GoNextCommand.ExecuteAsync(null);

        Assert.Empty(wizard.InstallOutput);
        Assert.False(wizard.HasInstallOutput);
        Assert.Equal(string.Empty, wizard.LatestInstallOutputLine);
        Assert.Contains(nameof(wizard.HasInstallOutput), changed);
        Assert.Contains(nameof(wizard.LatestInstallOutputLine), changed);
    }

    [Fact]
    public async Task DetectAgents_AppliesProbeResults_WithVersion()
    {
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.2.3");
        var wizard = CreateWizard(probe);

        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        Assert.Equal(AcpComponentAvailability.Installed, wizard.Agents[0].Availability);
        Assert.True(wizard.Agents[0].HasVersion);
        Assert.Equal("1.2.3", wizard.Agents[0].Version);
    }

    [Fact]
    public async Task DetectAgents_MissingRuntime_BlocksAdvancementUntilSelected()
    {
        var wizard = CreateWizard(new StubExecutableProbe());

        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        Assert.Equal(AcpComponentAvailability.Missing, wizard.Agents[0].Availability);
        // Agent absence must not be reported as adapter absence: the adapter was never probed.
        Assert.False(wizard.IsAdapterMissing);
        Assert.False(wizard.GoNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task DetectAgents_UndeterminedProbe_DoesNotBlockAdvancement()
    {
        // No process probing on this platform: the runtime is unknown, not absent.
        var probe = new StubExecutableProbe { SupportsProcessProbing = false };
        var wizard = CreateWizard(probe);

        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        Assert.Equal(AcpComponentAvailability.Undetermined, wizard.Agents[0].Availability);
        wizard.SelectedAgent = wizard.Agents[0];
        Assert.True(wizard.GoNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task GoNext_FromAgentSelection_PreselectsRecommendedAdapter_AndProbesIt()
    {
        var (wizard, _, _) = await WalkToComponentSetupAsync(
            configureProbe: probe =>
            {
                probe.SetNodePackage(AcpSetupWizardFixtures.AdapterPackage, true);
                return probe;
            });

        Assert.Equal(AcpSetupWizardStep.ComponentSetup, wizard.Step);
        Assert.Equal(2, wizard.Adapters.Count);
        // The built-in adapter ships with the agent, so the recommendation lands there first.
        Assert.Equal("adapter.builtin", wizard.SelectedAdapter?.Component.Id);
        Assert.NotNull(wizard.AdapterProbe);
        Assert.True(wizard.IsAdapterUsable);
        Assert.True(wizard.GoNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task ComponentSetup_MissingAdapterBlocksWalk_InstallReprobesAndReopens()
    {
        // The runtime is healthy; only the packaged adapter is absent, so its absence is what
        // blocks the walk. Package probing gates on its launcher being present too.
        var probe = ProbeForInstalledRuntime();
        probe.SetExecutable("npx", "/usr/bin/npx");
        var adapter = AcpSetupWizardFixtures.PackagedAdapter();
        var agent = AcpSetupWizardFixtures.Agent(adapters: adapter);
        var installer = new StubComponentInstaller();
        var wizard = CreateWizardFor(agent, probe, installer, localizer: null);
        probe.SetNodePackage(AcpSetupWizardFixtures.AdapterPackage, false);

        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = wizard.Agents[0];
        await wizard.GoNextCommand.ExecuteAsync(null); // → ComponentSetup + auto adapter probe

        Assert.True(wizard.IsAdapterMissing);
        Assert.False(wizard.GoNextCommand.CanExecute(null));

        // Installing flips the package answer; the orchestrator re-probes after the install.
        installer.OnInstall = _ => probe.SetNodePackage(AcpSetupWizardFixtures.AdapterPackage, true);

        await wizard.InstallAdapterCommand.ExecuteAsync(null);

        Assert.True(wizard.IsAdapterUsable);
        Assert.True(wizard.GoNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Parameters_BuildsRowsFromTemplate_PreviewsCommandLine()
    {
        var parameter = AcpSetupWizardFixtures.Parameter("--model", defaultValue: "sonnet");
        var (wizard, _, _) = await WalkToParametersAsync(parameters: new[] { parameter });

        Assert.Equal(AcpSetupWizardStep.Parameters, wizard.Step);
        var row = Assert.Single(wizard.Parameters);
        Assert.Equal("--model", row.Key);
        Assert.Equal("sonnet", row.Value);

        Assert.Contains("--model", wizard.LaunchCommandPreview, StringComparison.Ordinal);
        Assert.Contains("sonnet", wizard.LaunchCommandPreview, StringComparison.Ordinal);
        Assert.True(wizard.GoNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Parameters_MissingRequiredValue_StaysOnStepWithoutTesting()
    {
        var parameter = AcpSetupWizardFixtures.Parameter("--model", isRequired: true);
        var (wizard, tester, _) = await WalkToParametersAsync(parameters: new[] { parameter });
        var row = Assert.Single(wizard.Parameters);
        Assert.Empty(row.Value);

        await wizard.GoNextCommand.ExecuteAsync(null);

        Assert.Equal(AcpSetupWizardStep.Parameters, wizard.Step);
        Assert.True(row.HasValidationMessage);
        Assert.Equal(0, tester.TestCount);
    }

    [Fact]
    public async Task GoBack_RewindsOneStepAtATime_AndClearsErrorSurface()
    {
        var (wizard, tester, configuration) = await WalkToTestAsync();

        wizard.ErrorMessage = "boom";
        wizard.GoBackCommand.Execute(null);
        Assert.Equal(AcpSetupWizardStep.Parameters, wizard.Step);
        Assert.False(wizard.HasErrorMessage);

        wizard.GoBackCommand.Execute(null);
        Assert.Equal(AcpSetupWizardStep.ComponentSetup, wizard.Step);

        wizard.GoBackCommand.Execute(null);
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
        Assert.False(wizard.GoBackCommand.CanExecute(null));
    }

    [Fact]
    public async Task Test_SuccessfulHandshake_UnlocksSaveAndCarriesPlan()
    {
        var (wizard, tester, configuration) = await WalkToTestStepAsync();

        await wizard.TestCommand.ExecuteAsync(null);

        Assert.True(wizard.IsTestSuccessful);
        Assert.Equal(1, tester.TestCount);
        // The walk adapter is npx-packaged, so the plan launches through npx.
        Assert.NotNull(tester.LastPlan);
        Assert.Equal("npx", tester.LastPlan!.Command);
        Assert.Contains(AcpSetupWizardFixtures.AdapterPackage, tester.LastPlan!.Arguments, StringComparer.Ordinal);
        // The name is still empty at this point, so save stays locked until the user names it.
        Assert.False(wizard.SaveCommand.CanExecute(null));
        Assert.True(wizard.GoNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Test_HandshakeFailure_ShowsLocalizedStageAndRemediation_ThenRetestSucceeds()
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", StageHandshakeKey, "握手阶段");
        localizer.Set("zh-Hans", HandshakeRemediationKey, "请确认 agent 支持 ACP 协议");
        var (wizard, tester, _) = await WalkToTestAsync(localizer: localizer);
        tester.SetResult(AcpSetupTestResult.Failure(
            AcpSetupTestStage.Handshake,
            errorDetail: "agent closed the stream",
            remediationKey: HandshakeRemediationKey));

        await wizard.TestCommand.ExecuteAsync(null);

        Assert.False(wizard.IsTestSuccessful);
        Assert.Equal("握手阶段", wizard.TestFailureStageText);
        Assert.Equal("请确认 agent 支持 ACP 协议", wizard.TestRemediationText);
        Assert.False(wizard.SaveCommand.CanExecute(null));
        Assert.False(wizard.GoNextCommand.CanExecute(null));

        // Re-testing after a fix succeeds and clears the failure surface.
        tester.SetResult(AcpSetupWizardFixtures.WellKnownResults.Success());
        await wizard.TestCommand.ExecuteAsync(null);
        Assert.True(wizard.IsTestSuccessful);
        Assert.Equal(string.Empty, wizard.TestFailureStageText);
    }

    [Fact]
    public async Task Test_ValidationStageFailure_MirrorsOntoParameterRow()
    {
        var required = AcpSetupWizardFixtures.Parameter("--model", isRequired: true);
        var (wizard, tester, _) = await WalkToParametersAsync(parameters: new[] { required });
        var row = Assert.Single(wizard.Parameters);
        Assert.Empty(row.Value);

        // Testing straight from the form: the orchestrator validates before any process starts,
        // and the failure is mirrored onto the offending row instead of the test panel alone.
        await wizard.TestCommand.ExecuteAsync(null);

        Assert.False(wizard.IsTestSuccessful);
        Assert.True(row.HasValidationMessage);
        Assert.Equal(0, tester.TestCount);
    }

    [Fact]
    public async Task Save_AfterSuccessfulTest_PersistsStdioProfileShape()
    {
        var (wizard, tester, configuration) = await WalkToTestAsync();

        await wizard.GoNextCommand.ExecuteAsync(null); // → Save; name prefilled from the agent
        Assert.Equal("Test Agent", wizard.ProfileName);

        await wizard.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(configuration.Saved);
        Assert.Same(saved, wizard.SavedProfile);
        Assert.Equal("Test Agent", saved.Name);
        Assert.Equal(TransportType.Stdio, saved.Transport);
        Assert.Equal("npx", saved.StdioCommand);
        Assert.Contains(AcpSetupWizardFixtures.AdapterPackage, saved.StdioArguments!, StringComparer.Ordinal);
        Assert.True(wizard.IsOnSave);
    }

    [Fact]
    public async Task Save_RequiresSuccessfulTest_EvenWhenInvokedDirectly()
    {
        var (wizard, tester, configuration) = await WalkToTestStepAsync();

        Assert.False(wizard.SaveCommand.CanExecute(null), "an untested draft must not be savable");

        // Direct invocation bypasses CanExecute, so the handler itself must hold the line too.
        await wizard.SaveCommand.ExecuteAsync(null);

        Assert.Empty(configuration.Saved);
        Assert.Equal(0, tester.TestCount);
    }

    [Fact]
    public async Task Save_TrimsUserProvidedProfileName()
    {
        var (wizard, tester, configuration) = await WalkToTestAsync();

        await wizard.GoNextCommand.ExecuteAsync(null);
        wizard.ProfileName = "  我的工作台  ";
        await wizard.SaveCommand.ExecuteAsync(null);

        Assert.Equal("我的工作台", Assert.Single(configuration.Saved).Name);
    }

    [Fact]
    public async Task InstallRuntime_FailedInstall_SurfacesInstallerDetail()
    {
        var installer = new StubComponentInstaller(component =>
            AcpComponentInstallResult.Failure(component.Id, 1, output: null, "network down"));
        var wizard = CreateWizard(ProbeForInstalledRuntime(), installer);
        wizard.SelectedAgent = wizard.Agents[0];

        await wizard.InstallRuntimeCommand.ExecuteAsync(null);

        Assert.Equal(
            new[] { AcpSetupWizardFixtures.Runtime().Id },
            installer.InstalledComponentIds.ToArray());
        Assert.True(wizard.HasErrorMessage);
        Assert.Equal("network down", wizard.ErrorMessage);
    }

    // ── Shared walk helpers ─────────────────────────────────────────────────

    /// <summary>Detects the runtime, selects it, and walks to ComponentSetup with the adapter probed.</summary>
    private static Task<(AcpSetupWizardViewModel Wizard, StubComponentInstaller Installer, RecordingConfigurationService Configuration)> WalkToComponentSetupAsync(
        Func<StubExecutableProbe, StubExecutableProbe>? configureProbe = null,
        MutableTestCoreStringLocalizer? localizer = null)
    {
        var probe = ProbeForInstalledRuntime();
        if (configureProbe is not null)
        {
            configureProbe(probe);
        }

        return WalkWith(probe, localizer);
    }

    private static async Task<(AcpSetupWizardViewModel Wizard, StubComponentInstaller Installer, RecordingConfigurationService Configuration)> WalkWith(
        StubExecutableProbe probe,
        MutableTestCoreStringLocalizer? localizer)
    {
        var agent = AcpSetupWizardFixtures.Agent(
            adapters: new[] { AcpSetupWizardFixtures.BuiltInAdapter(), AcpSetupWizardFixtures.PackagedAdapter() });
        var installer = new StubComponentInstaller();
        var configuration = new RecordingConfigurationService();
        var wizard = CreateWizardFor(agent, probe, installer, localizer, configuration: configuration);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = wizard.Agents[0];
        await wizard.GoNextCommand.ExecuteAsync(null); // → ComponentSetup + auto adapter probe
        return (wizard, installer, configuration);
    }

    private static Task<(AcpSetupWizardViewModel Wizard, StubConnectivityTester Tester, RecordingConfigurationService Configuration)> WalkToParametersAsync(
        AcpSetupParameterDefinition[]? parameters = null,
        MutableTestCoreStringLocalizer? localizer = null)
    {
        var probe = ProbeForInstalledRuntime();
        var packagedAdapter = AcpSetupWizardFixtures.PackagedAdapter(
            parameters: parameters ?? Array.Empty<AcpSetupParameterDefinition>());
        return WalkToParametersAsync(probe, packagedAdapter, localizer);
    }

    private static async Task<(AcpSetupWizardViewModel Wizard, StubConnectivityTester Tester, RecordingConfigurationService Configuration)> WalkToParametersAsync(
        StubExecutableProbe probe,
        AcpAdapterDescriptor adapter,
        MutableTestCoreStringLocalizer? localizer)
    {
        var agent = AcpSetupWizardFixtures.Agent(adapters: adapter);
        var tester = new StubConnectivityTester(AcpSetupWizardFixtures.WellKnownResults.Success());
        var configuration = new RecordingConfigurationService();
        var wizard = CreateWizardFor(
            agent, probe, new StubComponentInstaller(), localizer, tester, configuration);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = wizard.Agents[0];
        await wizard.GoNextCommand.ExecuteAsync(null); // → ComponentSetup
        await wizard.GoNextCommand.ExecuteAsync(null); // → Parameters
        return (wizard, tester, configuration);
    }

    /// <summary>Walks to the Test step without running the test.</summary>
    private static async Task<(AcpSetupWizardViewModel Wizard, StubConnectivityTester Tester, RecordingConfigurationService Configuration)> WalkToTestStepAsync(
        AcpSetupParameterDefinition[]? parameters = null,
        MutableTestCoreStringLocalizer? localizer = null)
    {
        var (wizard, tester, configuration) = await WalkToParametersAsync(parameters, localizer);
        await wizard.GoNextCommand.ExecuteAsync(null); // → Test
        return (wizard, tester, configuration);
    }

    /// <summary>
    /// Walks to the Test step and completes one successful handshake, so save-step tests start from
    /// a verified configuration.
    /// </summary>
    private static async Task<(AcpSetupWizardViewModel Wizard, StubConnectivityTester Tester, RecordingConfigurationService Configuration)> WalkToTestAsync(
        AcpSetupParameterDefinition[]? parameters = null,
        MutableTestCoreStringLocalizer? localizer = null)
    {
        var walked = await WalkToTestStepAsync(parameters, localizer);
        await walked.Wizard.TestCommand.ExecuteAsync(null);
        return walked;
    }

    private static StubExecutableProbe ProbeForInstalledRuntime()
    {
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.0.0");
        return probe;
    }

    private static AcpSetupWizardViewModel CreateWizard(
        StubExecutableProbe? probe = null,
        StubComponentInstaller? installer = null,
        MutableTestCoreStringLocalizer? localizer = null)
        => CreateWizardFor(AcpSetupWizardFixtures.Agent(), probe ?? ProbeForInstalledRuntime(), installer ?? new StubComponentInstaller(), localizer);

    private static AcpSetupWizardViewModel CreateWizardFor(
        AcpAgentDescriptor agent,
        StubExecutableProbe probe,
        StubComponentInstaller installer,
        MutableTestCoreStringLocalizer? localizer,
        StubConnectivityTester? tester = null,
        RecordingConfigurationService? configuration = null)
        => AcpSetupWizardFixtures.CreateWizard(
            new StubAgentCatalog(agent),
            probe,
            installer,
            tester ?? new StubConnectivityTester(AcpSetupWizardFixtures.WellKnownResults.Success()),
            configuration ?? new RecordingConfigurationService(),
            localizer);
}
