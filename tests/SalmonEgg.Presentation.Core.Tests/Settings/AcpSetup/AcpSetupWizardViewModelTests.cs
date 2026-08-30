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

    /// <summary>
    /// A packaged adapter that npm reports installed must read as usable, and exactly one of the three
    /// state surfaces may be open at a time — the view binds each InfoBar to its own predicate, so two
    /// true at once would stack contradictory bars.
    /// </summary>
    [Fact]
    public async Task AdapterProbe_Installed_OpensOnlyTheReadySurface()
    {
        var (wizard, _) = await WalkToComponentSetupWithPackagedAdapterAsync(adapterInstalled: true);

        Assert.True(wizard.IsAdapterUsable);
        Assert.False(wizard.IsAdapterMissing);
        Assert.False(wizard.IsAdapterUndetermined);
        Assert.True(wizard.GoNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task AdapterProbe_Missing_OpensOnlyTheMissingSurface_AndBlocksAdvance()
    {
        var (wizard, _) = await WalkToComponentSetupWithPackagedAdapterAsync(adapterInstalled: false);

        Assert.True(wizard.IsAdapterMissing);
        Assert.False(wizard.IsAdapterUsable);
        Assert.False(wizard.IsAdapterUndetermined);
        Assert.False(wizard.GoNextCommand.CanExecute(null));
    }

    /// <summary>
    /// An unanswerable probe must not read as absence, and must not block: the wizard could not look, so
    /// the connection test is the real gate. This is the state the desktop hits when npm is installed but
    /// not on the launched process's PATH.
    /// </summary>
    [Fact]
    public async Task AdapterProbe_Undetermined_DoesNotClaimMissing_AndAllowsAdvance()
    {
        var (wizard, _) = await WalkToComponentSetupWithPackagedAdapterAsync(adapterInstalled: null);

        Assert.True(wizard.IsAdapterUndetermined);
        Assert.False(wizard.IsAdapterMissing);
        Assert.False(wizard.IsAdapterUsable);
        Assert.True(wizard.GoNextCommand.CanExecute(null));
    }

    /// <summary>
    /// The probe records why it reached its verdict, and that sentence is the difference between a red
    /// mark a user can act on and one they cannot. It shipped with zero consumers.
    /// </summary>
    [Fact]
    public async Task AdapterProbe_SurfacesTheDiagnosticDetail_WhenTheLauncherIsAbsent()
    {
        var adapter = AcpSetupWizardFixtures.PackagedAdapter();
        var agent = AcpSetupWizardFixtures.Agent(adapters: adapter);
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.0.0");
        // npx itself is absent, which is what a desktop launch without the user's shell PATH looks like.
        probe.SetExecutable("npx", path: null);
        var wizard = CreateWizardFor(agent, probe, new StubComponentInstaller(), localizer: null);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);
        await wizard.GoNextCommand.ExecuteAsync(null);

        Assert.True(wizard.IsAdapterMissing);
        Assert.True(wizard.HasAdapterProbeDetail);
        Assert.Contains("npx", wizard.AdapterProbeDetail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The row must say which command it looked for. Without it a red mark is unexplainable: the user
    /// cannot tell whether the wizard wanted <c>claude</c>, <c>npx</c>, or something else.
    /// </summary>
    [Fact]
    public void AgentRow_NamesTheCommandItProbes()
    {
        var wizard = CreateWizard();

        var row = Assert.Single(wizard.Agents);
        Assert.Equal(AcpSetupWizardFixtures.RuntimeCommand, row.ProbeCommand);
    }

    /// <summary>
    /// A path the user supplies is probed instead of the bare command name, so an executable that PATH
    /// cannot see becomes findable. This is the whole point of the override: a desktop process does not
    /// inherit the shell PATH where nvm and ~/.local/bin live.
    /// </summary>
    [Fact]
    public async Task AgentRow_CustomCommand_IsProbedInsteadOfTheCatalogName()
    {
        const string customPath = "/home/user/.nvm/versions/node/v24/bin/test-agent";
        var probe = new StubExecutableProbe();
        // The catalog name resolves to nothing, exactly as it does under a desktop-launched PATH.
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, path: null);
        probe.SetExecutable(customPath, customPath, "9.9.9");
        var wizard = CreateWizard(probe);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = Assert.Single(wizard.Agents);
        Assert.True(row.IsMissing);
        Assert.True(row.HasProbeDetail);

        row.CustomCommand = customPath;
        row.RequestVerify();

        Assert.False(wizard.IsBusy);
        Assert.True(row.IsInstalled);
        Assert.Equal("9.9.9", row.Version);
        Assert.Equal(customPath, row.ResolvedPath);
    }

    /// <summary>
    /// The override must reach the saved profile, not just detection. An override honoured only while
    /// probing yields a profile that passes its connection test and then fails every launch — worse than
    /// never offering the override.
    /// </summary>
    [Fact]
    public async Task Save_CarriesTheCustomCommand_IntoThePersistedProfile()
    {
        const string customNpx = "/home/user/.nvm/versions/node/v24/bin/npx";
        var adapter = AcpSetupWizardFixtures.PackagedAdapter();
        var agent = AcpSetupWizardFixtures.Agent(
            runtime: AcpSetupWizardFixtures.Runtime(),
            adapters: adapter);
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.0.0");
        // Only the user's path resolves; the bare launcher name does not.
        probe.SetExecutable(customNpx, customNpx);
        probe.SetNodePackage(AcpSetupWizardFixtures.AdapterPackage, installed: true);
        var tester = new StubConnectivityTester(StubConnectivityTester.SuccessfulHandshake());
        var configuration = new RecordingConfigurationService();
        var wizard = CreateWizardFor(
            agent,
            probe,
            new StubComponentInstaller(),
            localizer: null,
            tester,
            configuration);

        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = Assert.Single(wizard.Agents);
        wizard.SelectedAgent = row;
        await wizard.GoNextCommand.ExecuteAsync(null); // -> ComponentSetup
        // The launcher the plan runs is the adapter's, not the runtime's, so the override belongs here.
        Assert.Equal("npx", wizard.AdapterProbeCommand);
        wizard.AdapterCustomCommand = customNpx;
        await wizard.GoNextCommand.ExecuteAsync(null); // -> Parameters

        // The preview a user reviews before testing must already show the path they supplied.
        Assert.StartsWith(customNpx, wizard.LaunchCommandPreview, StringComparison.Ordinal);

        await wizard.GoNextCommand.ExecuteAsync(null); // -> Test
        await wizard.TestCommand.ExecuteAsync(null);
        Assert.True(wizard.IsTestSuccessful);
        // What was tested carries the override too, so the verdict is about the real command.
        Assert.Equal(customNpx, tester.LastPlan!.Command);

        await wizard.GoNextCommand.ExecuteAsync(null); // -> Save
        wizard.ProfileName = "Overridden";
        await wizard.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(configuration.Saved);
        Assert.Equal(customNpx, saved.StdioCommand);
    }

    /// <summary>
    /// A package coordinate is one installation source, not adapter identity. Detection accepts the
    /// adapter executable regardless of which package or vendor supplied it, and the resolved path must
    /// be the command tested and persisted when the GUI process PATH cannot resolve the bare name.
    /// </summary>
    [Fact]
    public async Task Save_WithExecutableAdapter_UsesTheResolvedCommandWithoutQueryingPackageIdentity()
    {
        const string resolvedAdapter = "/home/user/.nvm/versions/node/v24/bin/test-agent-acp";
        var adapter = AcpSetupWizardFixtures.ExecutableAdapter();
        var agent = AcpSetupWizardFixtures.Agent(adapters: adapter);
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.0.0");
        probe.SetExecutable(AcpSetupWizardFixtures.AdapterCommand, resolvedAdapter);
        var tester = new StubConnectivityTester(StubConnectivityTester.SuccessfulHandshake());
        var configuration = new RecordingConfigurationService();
        var wizard = CreateWizardFor(
            agent,
            probe,
            new StubComponentInstaller(),
            localizer: null,
            tester,
            configuration);

        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);
        await wizard.GoNextCommand.ExecuteAsync(null); // -> ComponentSetup

        Assert.True(wizard.IsAdapterUsable);
        Assert.Empty(probe.QueriedPackageManagers);

        await wizard.GoNextCommand.ExecuteAsync(null); // -> Parameters
        Assert.StartsWith(resolvedAdapter, wizard.LaunchCommandPreview, StringComparison.Ordinal);
        await wizard.GoNextCommand.ExecuteAsync(null); // -> Test
        await wizard.TestCommand.ExecuteAsync(null);

        Assert.True(wizard.IsTestSuccessful);
        Assert.Equal(resolvedAdapter, tester.LastPlan!.Command);

        await wizard.GoNextCommand.ExecuteAsync(null); // -> Save
        wizard.ProfileName = "Executable Adapter";
        await wizard.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(configuration.Saved);
        Assert.Equal(resolvedAdapter, saved.StdioCommand);
        Assert.Empty(saved.StdioArguments!);
    }

    /// <summary>
    /// Six of the eight shipped agents carry a built-in adapter, and the step rendered nothing at all for
    /// them: a title over an empty panel, correct but indistinguishable from a page that failed to load.
    /// Built-in is now its own state, separate from "installed", so the step can say why it is satisfied.
    /// </summary>
    [Fact]
    public async Task BuiltInAdapter_ReportsBuiltIn_NotInstalled_AndAllowsAdvance()
    {
        var agent = AcpSetupWizardFixtures.Agent(adapters: AcpSetupWizardFixtures.BuiltInAdapter());
        var wizard = CreateWizardFor(
            agent,
            ProbeForInstalledRuntime(),
            new StubComponentInstaller(),
            localizer: null);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);
        await wizard.GoNextCommand.ExecuteAsync(null);

        Assert.True(wizard.IsAdapterBuiltIn);
        Assert.False(wizard.IsAdapterInstalled);
        Assert.False(wizard.IsAdapterMissing);
        Assert.False(wizard.IsAdapterUndetermined);
        // A built-in adapter has no launcher to name, so the override disclosure stays hidden.
        Assert.False(wizard.HasAdapterProbeCommand);
        Assert.True(wizard.GoNextCommand.CanExecute(null));
    }

    /// <summary>
    /// A packaged adapter that npm reports present is "installed", not "built in": the two get different
    /// copy because one required the user to have something and the other did not. Installed also means
    /// found, so the "why wasn't it found?" override panel stays hidden — it exists for the missing case.
    /// </summary>
    [Fact]
    public async Task PackagedAdapter_ReportsInstalled_NotBuiltIn()
    {
        var (wizard, _) = await WalkToComponentSetupWithPackagedAdapterAsync(adapterInstalled: true);

        Assert.True(wizard.IsAdapterInstalled);
        Assert.False(wizard.IsAdapterBuiltIn);
        Assert.False(wizard.HasAdapterProbeCommand); // Found: no override panel about not finding it.
    }

    /// <summary>
    /// One install is the ordinary case, and it must not present a choice: the picker is gated on there
    /// actually being one, so a rare situation costs nothing in the common one.
    /// </summary>
    [Fact]
    public async Task AgentRow_WithOneInstall_OffersNoChoice()
    {
        var wizard = CreateWizard();
        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        var row = Assert.Single(wizard.Agents);
        Assert.True(row.IsInstalled);
        Assert.False(row.HasMultipleCandidates);
        Assert.Single(row.Candidates);
    }

    /// <summary>
    /// Shadowed installs are reported in PATH order, and the first is the one in use — which is what a
    /// shell would run. Without this the wizard silently picks one of several and never says so.
    /// </summary>
    [Fact]
    public async Task AgentRow_WithSeveralInstalls_ReportsThemInOrder_AndUsesTheFirst()
    {
        const string preferred = "/usr/local/bin/test-agent";
        const string shadowed = "/usr/bin/test-agent";
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, preferred, "1.0.0");
        probe.SetCandidates(AcpSetupWizardFixtures.RuntimeCommand, preferred, shadowed);
        var wizard = CreateWizard(probe);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        var row = Assert.Single(wizard.Agents);
        Assert.True(row.HasMultipleCandidates);
        Assert.Equal(new[] { preferred, shadowed }, row.Candidates);
        Assert.Equal(preferred, row.ResolvedPath);
        Assert.Equal(preferred, row.SelectedCandidate);
    }

    /// <summary>
    /// Picking the shadowed install must travel the same route a hand-typed path does — into the command
    /// overrides, and from there into the saved launch plan. A pick honoured only while probing would
    /// verify one executable and then start a different one.
    /// </summary>
    [Fact]
    public async Task AgentRow_PickingAShadowedInstall_BecomesTheCommandOverride()
    {
        const string preferred = "/usr/local/bin/test-agent";
        const string shadowed = "/opt/homebrew/bin/test-agent";
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, preferred, "1.0.0");
        probe.SetExecutable(shadowed, shadowed, "2.0.0");
        probe.SetCandidates(AcpSetupWizardFixtures.RuntimeCommand, preferred, shadowed);
        var wizard = CreateWizard(probe);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = Assert.Single(wizard.Agents);

        row.SelectedCandidate = shadowed;

        // Selecting re-probes the row, so the version shown is the one that install reports.
        Assert.False(wizard.IsBusy);
        Assert.Equal(shadowed, row.CustomCommand);
        Assert.Equal(shadowed, row.SelectedCandidate);
        Assert.Equal("2.0.0", row.Version);
    }

    /// <summary>
    /// Picking a candidate must not retract the choice: the picker has to survive being used, or the
    /// user gets exactly one switch and no way back.
    /// </summary>
    /// <remarks>
    /// The pick becomes an override, and re-probing an absolute path resolves one candidate by
    /// definition — so a row reading its candidate list straight off the latest probe drops to a single
    /// entry and hides the picker that produced the choice.
    /// </remarks>
    [Fact]
    public async Task AgentRow_PickingAShadowedInstall_KeepsTheChoiceAvailable()
    {
        const string preferred = "/usr/local/bin/test-agent";
        const string shadowed = "/opt/homebrew/bin/test-agent";
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, preferred, "1.0.0");
        probe.SetExecutable(shadowed, shadowed, "2.0.0");
        probe.SetCandidates(AcpSetupWizardFixtures.RuntimeCommand, preferred, shadowed);
        var wizard = CreateWizard(probe);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = Assert.Single(wizard.Agents);

        row.SelectedCandidate = shadowed;

        Assert.True(row.HasMultipleCandidates);
        Assert.Equal(new[] { preferred, shadowed }, row.Candidates);

        // And the switch is reversible, which is the point of keeping the list.
        row.SelectedCandidate = preferred;

        Assert.Equal(preferred, row.SelectedCandidate);
        Assert.True(row.HasMultipleCandidates);
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
    public async Task ComponentSetup_ChangingAdapter_ReprobesTheNewSelection()
    {
        var (wizard, _, _) = await WalkToComponentSetupAsync(
            configureProbe: probe =>
            {
                probe.SetNodePackage(AcpSetupWizardFixtures.AdapterPackage, false);
                return probe;
            });

        // The recommended built-in adapter was selected and is usable. Picking the packaged adapter
        // must invalidate that verdict and use the package answer for the newly selected component.
        wizard.SelectedAdapter = wizard.Adapters.Single(adapter => adapter.Component.Id == "adapter.packaged");
        if (wizard.DetectAdapterCommand.ExecutionTask is { } execution)
        {
            await execution;
        }

        Assert.Equal("adapter.packaged", wizard.SelectedAdapter?.Component.Id);
        Assert.True(wizard.IsAdapterMissing);
        Assert.False(wizard.GoNextCommand.CanExecute(null));
    }

    /// <summary>
    /// The step position resolves a parameterized resource. The key lives in CoreStrings (the layer
    /// view models localize against); before this test the value was assembled by string surgery on
    /// a UI-layer key the resolver could never find, so every language silently showed English.
    /// </summary>
    [Fact]
    public async Task StepPositionText_ResolvesParameterizedKey_PerLanguage()
    {
        var zh = new MutableTestCoreStringLocalizer();
        zh.Set("zh-Hans", "AcpSetup_Step_Position", "第 {0} 步，共 {1} 步");
        var (wizardZh, _, _) = await WalkToComponentSetupAsync(localizer: zh);
        Assert.Equal("第 2 步，共 5 步", wizardZh.StepPositionText);

        // Without a localizer the fallback template formats, so the position is still stated.
        var (wizardFallback, _, _) = await WalkToComponentSetupAsync(localizer: null);
        Assert.Equal("Step 2 of 5", wizardFallback.StepPositionText);
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
        Assert.Contains("/test/bin/npm", wizard.AdapterProbeDetail, StringComparison.Ordinal);
        Assert.False(wizard.GoNextCommand.CanExecute(null));

        // Installing flips the package answer; the orchestrator re-probes after the install.
        installer.OnInstall = _ => probe.SetNodePackage(AcpSetupWizardFixtures.AdapterPackage, true);

        await wizard.InstallAdapterCommand.ExecuteAsync(null);

        Assert.True(wizard.IsAdapterUsable);
        Assert.Equal("/test/node_modules", wizard.AdapterPackageLocation);
        Assert.True(wizard.HasAdapterPackageLocation);
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

    /// <summary>
    /// The failure banner binds to IsTestFailed, not HasTestResult: a passing test also produces a
    /// result, so a banner keyed on "a result exists" opens on success too. Both directions are
    /// asserted because the bug is symmetric — the banner must be shut when the test passes.
    /// </summary>
    [Fact]
    public async Task IsTestFailed_TracksFailureOnly_AndClearsOnRetest()
    {
        var (wizard, tester, _) = await WalkToTestStepAsync();

        // Before any test there is neither a result nor a verdict.
        Assert.False(wizard.HasTestResult);
        Assert.False(wizard.IsTestFailed);

        tester.SetResult(AcpSetupWizardFixtures.WellKnownResults.Success());
        await wizard.TestCommand.ExecuteAsync(null);
        Assert.True(wizard.IsTestSuccessful);
        Assert.True(wizard.HasTestResult);
        Assert.False(wizard.IsTestFailed); // The regression this pins: success must not open the error bar.

        tester.SetResult(AcpSetupTestResult.Failure(
            AcpSetupTestStage.Handshake,
            errorDetail: "agent closed the stream",
            remediationKey: HandshakeRemediationKey));
        await wizard.TestCommand.ExecuteAsync(null);
        Assert.False(wizard.IsTestSuccessful);
        Assert.True(wizard.IsTestFailed);
    }

    /// <summary>
    /// ErrorDetail is raw platform English (stderr excerpt, protocol error). It reaches the screen as
    /// small print under the localized advice, so it must pass through on failure and vanish on
    /// success — a stale excerpt lingering after a passing retest would describe a problem that is gone.
    /// </summary>
    [Fact]
    public async Task TestErrorDetail_CarriesRawFailureDetail_AndEmptiesOnSuccess()
    {
        var (wizard, tester, _) = await WalkToTestStepAsync();

        Assert.Equal(string.Empty, wizard.TestErrorDetail);
        Assert.False(wizard.HasTestErrorDetail);

        const string detail = "agent closed the stream";
        tester.SetResult(AcpSetupTestResult.Failure(
            AcpSetupTestStage.Handshake,
            errorDetail: detail,
            remediationKey: HandshakeRemediationKey));
        await wizard.TestCommand.ExecuteAsync(null);
        Assert.Equal(detail, wizard.TestErrorDetail);
        Assert.True(wizard.HasTestErrorDetail);

        tester.SetResult(AcpSetupWizardFixtures.WellKnownResults.Success());
        await wizard.TestCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, wizard.TestErrorDetail);
        Assert.False(wizard.HasTestErrorDetail);
    }

    /// <summary>
    /// An installed adapter has a launcher just like a missing one does, but offering "why wasn't it
    /// found?" about a component that was found is noise. The override panel gates on the probe having
    /// failed to confirm presence, not merely on a launcher existing.
    /// </summary>
    [Fact]
    public async Task HasAdapterProbeCommand_IsFalse_WhenAdapterIsAlreadyUsable()
    {
        // Installed adapter: launcher named ("npx") yet present, so no override panel.
        var (installed, _) = await WalkToComponentSetupWithPackagedAdapterAsync(adapterInstalled: true);
        Assert.Equal("npx", installed.AdapterProbeCommand);
        Assert.True(installed.IsAdapterUsable);
        Assert.False(installed.HasAdapterProbeCommand);

        // Missing adapter: same launcher shape, but nothing found, so the panel appears.
        var (missing, _) = await WalkToComponentSetupWithPackagedAdapterAsync(adapterInstalled: false);
        Assert.False(missing.IsAdapterUsable);
        Assert.True(missing.HasAdapterProbeCommand);
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
    public async Task InstallAgentRow_FailedInstall_SurfacesInstallerDetail()
    {
        var installer = new StubComponentInstaller(component =>
            AcpComponentInstallResult.Failure(component.Id, 1, output: null, "network down"));
        var wizard = CreateWizard(ProbeForInstalledRuntime(), installer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        Assert.Single(wizard.Agents).RequestInstall();

        Assert.Equal(
            new[] { AcpSetupWizardFixtures.Runtime().Id },
            installer.InstalledComponentIds.ToArray());
        Assert.True(wizard.HasErrorMessage);
        Assert.Equal("network down", wizard.ErrorMessage);
    }

    /// <summary>
    /// A row offers its install button on the component's own <c>SupportsAutomaticInstall</c>, which says
    /// nothing about whether this platform can install anything at all. On a platform whose installer
    /// declines every request, the request must be refused before it reaches the installer — otherwise
    /// the wizard runs an install it knows cannot work and reports the installer's refusal as a failure
    /// the user is invited to retry.
    /// </summary>
    [Fact]
    public async Task InstallAgentRow_WhenPlatformCannotInstall_IsRefusedWithoutCallingTheInstaller()
    {
        var installer = new StubComponentInstaller(supportsAutomaticInstall: false);
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, path: null);
        var wizard = CreateWizard(probe, installer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = Assert.Single(wizard.Agents);
        Assert.True(row.IsMissing);

        row.RequestInstall();

        Assert.Empty(installer.InstalledComponentIds);
        Assert.False(wizard.IsBusy);
        Assert.False(wizard.HasInstallOutput);
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

    /// <summary>
    /// Walks to the component step with a npx-packaged adapter whose package query answers
    /// <paramref name="adapterInstalled"/> — true, false, or null for "could not tell".
    /// </summary>
    private static async Task<(AcpSetupWizardViewModel Wizard, StubComponentInstaller Installer)> WalkToComponentSetupWithPackagedAdapterAsync(
        bool? adapterInstalled)
    {
        var adapter = AcpSetupWizardFixtures.PackagedAdapter();
        var agent = AcpSetupWizardFixtures.Agent(adapters: adapter);
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.0.0");
        probe.SetExecutable("npx", "/usr/bin/npx");
        probe.SetNodePackage(AcpSetupWizardFixtures.AdapterPackage, adapterInstalled);
        var installer = new StubComponentInstaller();
        var wizard = CreateWizardFor(agent, probe, installer, localizer: null);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);
        await wizard.GoNextCommand.ExecuteAsync(null);
        return (wizard, installer);
    }

    /// <summary>
    /// A machine with the runtime installed and its package manager beside it.
    /// </summary>
    /// <remarks>
    /// The sibling entry is load-bearing, not padding. An install derives its package manager from the
    /// resolved launcher's own directory and uses only that candidate, so a machine whose npm is somewhere
    /// else genuinely cannot install this component — and the wizard now withholds the offer instead of
    /// <summary>
    /// A second install starts from an empty log rather than appending to the previous one's.
    /// </summary>
    /// <remarks>
    /// The output surface is shared by both install entry points, so a log that carried over would show one
    /// component's installer lines under another component's name. Clearing on step entry alone did not
    /// cover this: two installs can happen without ever leaving the selection step.
    /// </remarks>
    [Fact]
    public async Task ASecondInstall_ShouldNotInheritTheFirstInstallsOutput()
    {
        var probe = ProbeForInstalledRuntime();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, null);
        var installer = new StubComponentInstaller();
        installer.OutputLines.Add("first install line");
        var wizard = CreateWizard(probe, installer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = wizard.Agents[0];

        row.RequestInstall();
        Assert.False(wizard.IsBusy);
        Assert.Equal(new[] { "first install line" }, wizard.InstallOutput);

        installer.OutputLines.Clear();
        installer.OutputLines.Add("second install line");

        row.RequestInstall();
        Assert.False(wizard.IsBusy);

        Assert.Equal(new[] { "second install line" }, wizard.InstallOutput);
        Assert.Equal("second install line", wizard.LatestInstallOutputLine);
    }

    // ── Cancelling a running operation ──────────────────────────────────────

    /// <summary>
    /// Cancelling reaches an install started from an agent row.
    /// </summary>
    /// <remarks>
    /// The row is the case a per-command token cannot serve: its button raises an event rather than binding
    /// a command, so the install was previously started with <c>CancellationToken.None</c> and the only
    /// thing that could end it was the installer's own ten-minute timeout. Cancellation therefore lives on
    /// the shared operation scope, which every entry point already goes through.
    /// </remarks>
    [Fact]
    public async Task CancelOperation_ShouldCancelAnInstallStartedFromAnAgentRow()
    {
        var probe = ProbeForInstalledRuntime();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, null);
        var installer = new StubComponentInstaller();
        var wizard = CreateWizard(probe, installer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        // Cancelled from inside the install, which is where a user would press it: while it runs.
        installer.OnInstallWithToken += _ => wizard.CancelOperationCommand.Execute(null);

        wizard.Agents[0].RequestInstall();

        var token = Assert.Single(installer.ReceivedTokens);
        Assert.True(token.IsCancellationRequested);
        // Cancellation is a user action, so it leaves no error on screen and does not wedge the wizard.
        Assert.False(wizard.IsBusy);
        Assert.False(wizard.HasErrorMessage);
    }

    /// <summary>
    /// Reverse verification: without pressing cancel the same install runs with an uncancelled token, so
    /// the assertion above is about cancellation rather than about a token that arrives cancelled anyway.
    /// </summary>
    [Fact]
    public async Task WithoutCancelling_AnInstallShouldRunWithALiveToken()
    {
        var probe = ProbeForInstalledRuntime();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, null);
        var installer = new StubComponentInstaller();
        var wizard = CreateWizard(probe, installer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        wizard.Agents[0].RequestInstall();

        Assert.False(Assert.Single(installer.ReceivedTokens).IsCancellationRequested);
    }

    /// <summary>
    /// Cancel is offered exactly while something is running: enabled during an operation, and not before
    /// or after, so it is never a button that does nothing.
    /// </summary>
    [Fact]
    public async Task CancelOperation_ShouldOnlyBeExecutableWhileBusy()
    {
        var probe = ProbeForInstalledRuntime();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, null);
        var installer = new StubComponentInstaller();
        var wizard = CreateWizard(probe, installer);
        Assert.False(wizard.CancelOperationCommand.CanExecute(null));

        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var executableDuringInstall = false;
        installer.OnInstallWithToken += _ => executableDuringInstall = wizard.CancelOperationCommand.CanExecute(null);

        wizard.Agents[0].RequestInstall();

        Assert.True(executableDuringInstall);
        Assert.False(wizard.CancelOperationCommand.CanExecute(null));
    }

    /// <summary>
    /// Cancelling with nothing running is a no-op rather than a crash: the button's enablement and the
    /// command's own behaviour are separate guarantees, and only the latter holds on every platform where a
    /// stale binding could still invoke it.
    /// </summary>
    [Fact]
    public void CancelOperation_WithNothingRunning_ShouldDoNothing()
    {
        var wizard = CreateWizard();

        wizard.CancelOperationCommand.Execute(null);

        Assert.False(wizard.IsBusy);
        Assert.False(wizard.HasErrorMessage);
    }

    /// <summary>
    /// A cancel arriving while an operation is still unwinding is harmless.
    /// </summary>
    /// <remarks>
    /// The real dispatcher marshals every UI write to another thread, so <c>IsBusy = false</c> lands after
    /// the operation's cleanup has already released its cancellation source. The button a user sees is
    /// therefore still enabled for a moment after there is anything left to cancel, and a click in that
    /// window must not reach a disposed source. Clearing the field before disposing it is what makes the
    /// click a no-op instead of an <see cref="ObjectDisposedException"/> inside a command handler.
    ///
    /// Modelled with a queueing dispatcher because an immediate one closes the window by construction: it
    /// applies the busy flag inline, so the button is already disabled.
    /// </remarks>
    [Fact]
    public async Task CancelOperation_ArrivingAsAnOperationWindsDown_ShouldBeHarmless()
    {
        var dispatcher = new QueueingUiDispatcher();
        var probe = ProbeForInstalledRuntime();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, null);
        var wizard = AcpSetupWizardFixtures.CreateWizard(
            new StubAgentCatalog(AcpSetupWizardFixtures.Agent()),
            probe,
            new StubComponentInstaller(),
            new StubConnectivityTester(AcpSetupWizardFixtures.WellKnownResults.Success()),
            new RecordingConfigurationService(),
            localizer: null,
            dispatcher);

        var detecting = wizard.DetectAgentsCommand.ExecuteAsync(null);

        // Pump the queue, pressing cancel from inside every callback. One of those callbacks is the
        // cleanup's own IsBusy = false, which runs between the wizard releasing its cancellation source and
        // disposing it — the precise window a user's click can land in, and the only one where the two
        // orderings of that release differ. Driving it from inside the callback makes hitting it certain
        // rather than a matter of thread timing.
        while (!detecting.IsCompleted)
        {
            while (dispatcher.PendingCount > 0)
            {
                if (wizard.CancelOperationCommand.CanExecute(null))
                {
                    wizard.CancelOperationCommand.Execute(null);
                }

                dispatcher.RunNext();
                if (wizard.CancelOperationCommand.CanExecute(null))
                {
                    wizard.CancelOperationCommand.Execute(null);
                }
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        dispatcher.RunAll();
        Assert.False(wizard.IsBusy);
        Assert.False(wizard.HasErrorMessage);
    }

    // ── Re-detection sees the machine as it is now ──────────────────────────

    /// <summary>
    /// Detecting on the user's request must search the machine again, not answer from a cached search.
    /// </summary>
    /// <remarks>
    /// This is the wizard's own loop: it reports a toolchain missing, points the user at its documentation,
    /// and offers "detect again". The search behind that is cached — it spawns a login shell and walks the
    /// filesystem, which is too expensive to repeat per component — so without this the button re-ran a
    /// detection against the machine as it was <em>before</em> the user installed anything, and reported the
    /// toolchain missing however many times they pressed it. The only way out was restarting the app.
    /// </remarks>
    [Fact]
    public async Task DetectAgents_ShouldInvalidateTheSearchFirst()
    {
        var probe = ProbeForInstalledRuntime();
        var wizard = CreateWizard(probe);

        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        Assert.Equal(1, probe.InvalidateCount);
    }

    /// <summary>
    /// Re-detecting the adapter does the same, since it backs the "detect again" button beside the
    /// adapter's own toolchain-missing surface.
    /// </summary>
    [Fact]
    public async Task DetectAdapter_ShouldInvalidateTheSearchFirst()
    {
        var probe = ProbeForInstalledRuntime();
        var wizard = CreateWizard(probe);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = wizard.Agents[0];
        await wizard.GoNextCommand.ExecuteAsync(null);
        var afterWalk = probe.InvalidateCount;

        await wizard.DetectAdapterCommand.ExecuteAsync(null);

        Assert.True(
            probe.InvalidateCount > afterWalk,
            $"expected re-detection to invalidate the search; count stayed at {afterWalk}.");
    }

    /// <summary>
    /// An install changes the machine, so the re-probe that follows it must not reuse the search from
    /// before.
    /// </summary>
    /// <remarks>
    /// A package manager writes the new executable into its own bin directory, and a first global install
    /// can create that directory outright — which a directory list captured beforehand cannot contain. The
    /// re-probe would then contradict an install that had just succeeded.
    /// </remarks>
    [Fact]
    public async Task InstallAgentRow_ShouldInvalidateTheSearchBeforeReprobing()
    {
        var probe = ProbeForInstalledRuntime();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, null);
        var installer = new StubComponentInstaller();
        var wizard = CreateWizard(probe, installer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = wizard.Agents[0];
        var afterDetect = probe.InvalidateCount;
        installer.OnInstall += _ =>
            probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.0.0");

        row.RequestInstall();
        // Inline completion: the stub installer and the test dispatcher are both synchronous, so a settled
        // busy flag is proof the whole chain finished rather than a race.
        Assert.False(wizard.IsBusy);

        Assert.True(
            probe.InvalidateCount > afterDetect,
            $"expected the install's re-probe to invalidate the search; count stayed at {afterDetect}.");
        Assert.True(row.IsInstalled);
    }

    /// discovering it on click. Registering the sibling is what makes this the ordinary machine the
    /// install tests mean to describe.
    /// </remarks>
    private static StubExecutableProbe ProbeForInstalledRuntime()
    {
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.0.0");
        probe.SetExecutable("/usr/bin/npm", "/usr/bin/npm");
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

    // ── Missing toolchain ───────────────────────────────────────────────────

    /// <summary>
    /// The shipped defect: a row whose agent is missing offered an enabled install button on a machine
    /// with no package manager at all, and the absence only surfaced as a failed install after the click.
    /// </summary>
    /// <remarks>
    /// Asserts the offer is withdrawn <em>and</em> that the row says why — withdrawing it alone would
    /// leave the row looking like the agent cannot be installed, which is a different, wrong conclusion.
    /// </remarks>
    [Fact]
    public async Task DetectAgents_WithNoPackageManager_WithdrawsTheInstallOfferAndExplainsWhy()
    {
        var probe = new StubExecutableProbe().WithoutPackageManagers();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, path: null);
        var installer = new StubComponentInstaller();
        var wizard = CreateWizard(probe, installer);

        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        var row = Assert.Single(wizard.Agents);
        Assert.True(row.IsMissing);
        Assert.False(row.CanInstallHere);
        Assert.True(row.IsToolchainMissing);
        Assert.Equal("Node.js", row.MissingToolchainName);
        Assert.NotNull(row.ToolchainDocumentation);
        // The row still reports an install path exists, so the view can tell "we never automate this"
        // from "we would, but not on this machine".
        Assert.True(row.HasAutomaticInstallPath);
    }

    /// <summary>
    /// Reverse verification for the test above: the very same walk on a machine that <em>does</em> have a
    /// package manager must keep offering the install. Without this, a bug that withheld the button
    /// unconditionally would pass the assertions above.
    /// </summary>
    [Fact]
    public async Task DetectAgents_WithPackageManagerPresent_StillOffersTheInstall()
    {
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, path: null);
        var wizard = CreateWizard(probe, new StubComponentInstaller());

        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        var row = Assert.Single(wizard.Agents);
        Assert.True(row.IsMissing);
        Assert.True(row.CanInstallHere);
        Assert.False(row.IsToolchainMissing);
        Assert.Empty(row.MissingToolchainName);
        Assert.Null(row.ToolchainDocumentation);
        Assert.Empty(row.ToolchainMissingHint);
    }

    /// <summary>
    /// The install request itself must be refused, not merely hidden. The row's button raises an event
    /// rather than binding a command, so there is no CanExecute to stop it — a caller reaching the
    /// handler directly (or a stale view) would otherwise run an install that cannot succeed.
    /// </summary>
    [Fact]
    public async Task InstallAgentRow_WithNoPackageManager_IsRefusedWithoutCallingTheInstaller()
    {
        var probe = new StubExecutableProbe().WithoutPackageManagers();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, path: null);
        var installer = new StubComponentInstaller();
        installer.OutputLines.Add("added 1 package");
        var wizard = CreateWizard(probe, installer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        Assert.Single(wizard.Agents).RequestInstall();

        Assert.Empty(installer.InstalledComponentIds);
        Assert.False(wizard.HasInstallOutput);
        Assert.False(wizard.IsBusy);
    }

    /// <summary>
    /// A missing toolchain must reach the user as localized advice naming the toolchain, not as the
    /// platform layer's raw diagnostic naming a package-manager executable.
    /// </summary>
    /// <remarks>
    /// The installer is the real desktop one's contract rather than a bare stub: the key and the toolchain
    /// name travel on <see cref="AcpComponentInstallResult"/>, and this asserts the wizard prefers them
    /// over <c>ErrorDetail</c>. The raw detail is what shipped, and it was untranslated.
    /// </remarks>
    [Fact]
    public async Task InstallAgentRow_ToolchainFailure_SurfacesLocalizedAdviceRatherThanRawDetail()
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "AcpSetup_Install_ToolchainMissing", "先装 {0} 再来。");
        var installer = new StubComponentInstaller(component =>
            AcpComponentInstallResult.Failure(
                component.Id,
                exitCode: null,
                output: null,
                errorDetail: "'npm' was not found on PATH.",
                remediationKey: "AcpSetup_Install_ToolchainMissing",
                missingToolchainName: "Node.js"));
        var wizard = CreateWizard(ProbeForInstalledRuntime(), installer, localizer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        Assert.Single(wizard.Agents).RequestInstall();

        Assert.Equal("先装 Node.js 再来。", wizard.ErrorMessage);
        // The executable-naming diagnostic must not be what the user reads.
        Assert.DoesNotContain("npm", wizard.ErrorMessage);
    }

    /// <summary>
    /// Reverse verification: a failure carrying no remediation key still surfaces its raw detail, so the
    /// change above narrowed the message for one cause rather than swallowing every other one.
    /// </summary>
    [Fact]
    public async Task InstallAgentRow_FailureWithoutRemediationKey_StillSurfacesRawDetail()
    {
        var installer = new StubComponentInstaller(component =>
            AcpComponentInstallResult.Failure(component.Id, 1, output: null, "network down"));
        var wizard = CreateWizard(ProbeForInstalledRuntime(), installer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        Assert.Single(wizard.Agents).RequestInstall();

        Assert.Equal("network down", wizard.ErrorMessage);
    }

    /// <summary>
    /// An undetermined toolchain probe must not withhold the offer, matching how an undetermined component
    /// probe does not block the walk: an unanswerable probe is not evidence of absence, and the installer's
    /// own report is a better answer than refusing up front.
    /// </summary>
    [Fact]
    public async Task DetectAgents_WhenProbingIsUnsupported_StillOffersTheInstall()
    {
        var probe = new StubExecutableProbe().WithoutPackageManagers();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, path: null);
        probe.SupportsProcessProbing = false;
        var wizard = CreateWizard(probe, new StubComponentInstaller());

        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        var row = Assert.Single(wizard.Agents);
        Assert.True(row.IsUndetermined);
        Assert.False(row.IsToolchainMissing);
        Assert.True(row.CanInstallHere);
    }

    /// <summary>
    /// Installing a toolchain out of band and re-detecting must restore the offer. Without a re-probe the
    /// row would keep the stale verdict and the user could never get the button back.
    /// </summary>
    [Fact]
    public async Task DetectAgents_AfterToolchainAppears_RestoresTheInstallOffer()
    {
        var probe = new StubExecutableProbe().WithoutPackageManagers();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, path: null);
        var wizard = CreateWizard(probe, new StubComponentInstaller());
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = Assert.Single(wizard.Agents);
        Assert.True(row.IsToolchainMissing);

        probe.SetExecutable("npm", "/usr/local/bin/npm");
        await wizard.DetectAgentsCommand.ExecuteAsync(null);

        Assert.False(row.IsToolchainMissing);
        Assert.True(row.CanInstallHere);
    }

    /// <summary>
    /// The adapter step's install command is gated the same way. Its button is command-bound, so this
    /// asserts CanExecute goes false and that the step names the toolchain the user has to install.
    /// </summary>
    [Fact]
    public async Task AdapterStep_WithNoPackageManager_DisablesInstallAndNamesTheToolchain()
    {
        var agent = AcpSetupWizardFixtures.Agent(
            adapters: new[] { AcpSetupWizardFixtures.PackagedAdapter() });
        var probe = new StubExecutableProbe().WithoutPackageManagers();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.0.0");
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "AcpSetup_Adapter_ToolchainMissing", "缺少 {0}。");
        var wizard = CreateWizardFor(agent, probe, new StubComponentInstaller(), localizer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);

        await wizard.GoNextCommand.ExecuteAsync(null);

        Assert.True(wizard.IsAdapterMissing);
        Assert.True(wizard.IsAdapterToolchainMissing);
        Assert.Equal("Node.js", wizard.MissingAdapterToolchainName);
        Assert.Equal("缺少 Node.js。", wizard.AdapterToolchainMissingText);
        Assert.NotNull(wizard.AdapterToolchainDocumentation);
        Assert.False(wizard.InstallAdapterCommand.CanExecute(null));

        // Direct invocation bypasses CanExecute, so the handler must hold the line too.
        await wizard.InstallAdapterCommand.ExecuteAsync(null);
        Assert.Empty(wizard.InstallOutput);
    }

    /// <summary>
    /// Reverse verification for the adapter gate: with a package manager present the same walk keeps the
    /// install enabled and raises none of the toolchain surface.
    /// </summary>
    [Fact]
    public async Task AdapterStep_WithPackageManagerPresent_KeepsInstallEnabled()
    {
        var agent = AcpSetupWizardFixtures.Agent(
            adapters: new[] { AcpSetupWizardFixtures.PackagedAdapter() });
        var probe = ProbeForInstalledRuntime();
        probe.SetExecutable("npx", "/usr/bin/npx");
        probe.SetNodePackage(AcpSetupWizardFixtures.AdapterPackage, installed: false);
        var wizard = CreateWizardFor(agent, probe, new StubComponentInstaller(), localizer: null);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);

        await wizard.GoNextCommand.ExecuteAsync(null);

        Assert.True(wizard.IsAdapterMissing);
        Assert.False(wizard.IsAdapterToolchainMissing);
        Assert.Empty(wizard.AdapterToolchainMissingText);
        Assert.True(wizard.InstallAdapterCommand.CanExecute(null));
    }

    /// <summary>
    /// A built-in adapter has no toolchain to need, so the toolchain surface must stay silent. This is the
    /// case a naive "no manager on PATH means missing toolchain" rule would get wrong, telling most of the
    /// catalog's users to install Node for an adapter that installs nothing.
    /// </summary>
    [Fact]
    public async Task AdapterStep_BuiltInAdapter_NeverReportsAMissingToolchain()
    {
        var probe = new StubExecutableProbe().WithoutPackageManagers();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/usr/bin/test-agent", "1.0.0");
        var wizard = CreateWizard(probe, new StubComponentInstaller());
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);

        await wizard.GoNextCommand.ExecuteAsync(null);

        Assert.True(wizard.IsAdapterBuiltIn);
        Assert.False(wizard.IsAdapterToolchainMissing);
        Assert.Empty(wizard.AdapterToolchainMissingText);
    }
}
