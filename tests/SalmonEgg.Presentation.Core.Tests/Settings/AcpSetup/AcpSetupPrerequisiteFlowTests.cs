using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;
using SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;

namespace SalmonEgg.Presentation.Core.Tests.Settings.AcpSetup;

public sealed class AcpSetupPrerequisiteFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GoNext_FreshMachine_RequiresToolchainThenAgentThenAdapter(bool separateAdapter)
    {
        // Arrange
        var probe = new StubExecutableProbe().WithoutPackageManagers();
        var operations = new List<string>();
        var toolchainInstaller = new StubToolchainInstaller(requirement =>
        {
            operations.Add(requirement.DisplayName);
            probe.SetExecutable("npm", "/test/bin/npm");
        });
        var installer = new StubComponentInstaller();
        installer.OnInstall = component =>
        {
            operations.Add(component.Id);
            probe.SetExecutable(component.ProbeCommand, "/test/bin/" + component.ProbeCommand);
        };
        var adapter = separateAdapter
            ? AcpSetupWizardFixtures.ExecutableAdapter()
            : AcpSetupWizardFixtures.BuiltInAdapter();
        var agent = AcpSetupWizardFixtures.Agent(adapters: adapter);
        var tester = new StubConnectivityTester(StubConnectivityTester.SuccessfulHandshake());
        var configuration = new RecordingConfigurationService();
        var wizard = AcpSetupWizardFixtures.CreateWizard(
            new StubAgentCatalog(agent), probe, installer, tester, configuration,
            toolchainInstaller: toolchainInstaller);
        var row = Assert.Single(wizard.Agents);
        wizard.SelectedAgent = row;

        // Act / Assert: each next action is offered only after its prerequisite is verified.
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
        Assert.True(row.CanInstallToolchainHere);
        Assert.False(row.CanInstallHere);
        row.RequestInstall();
        Assert.Empty(operations);

        row.RequestToolchainInstall();
        Assert.False(wizard.IsBusy);
        Assert.Equal(new[] { "Node.js" }, operations);
        Assert.False(row.CanInstallToolchainHere);
        Assert.True(row.CanInstallHere);
        Assert.False(wizard.GoNextCommand.CanExecute(null));
        Assert.Null(wizard.AdapterProbe);

        row.RequestInstall();
        Assert.False(wizard.IsBusy);
        Assert.True(row.IsInstalled);
        Assert.True(wizard.GoNextCommand.CanExecute(null));
        Assert.Equal(new[] { "Node.js", agent.Runtime.Id }, operations);
        Assert.Null(wizard.AdapterProbe);

        await wizard.GoNextCommand.ExecuteAsync(null);
        if (separateAdapter)
        {
            Assert.Equal(AcpSetupWizardStep.ComponentSetup, wizard.Step);
            Assert.True(wizard.IsAdapterMissing);
            Assert.False(wizard.GoNextCommand.CanExecute(null));
            await wizard.InstallAdapterCommand.ExecuteAsync(null);
            Assert.True(wizard.IsAdapterInstalled);
            Assert.Equal(new[] { "Node.js", agent.Runtime.Id, adapter.Component.Id }, operations);
            await wizard.GoNextCommand.ExecuteAsync(null);
        }
        else
        {
            Assert.True(wizard.IsAdapterBuiltIn);
            Assert.Equal(new[] { "Node.js", agent.Runtime.Id }, operations);
        }

        Assert.Equal(AcpSetupWizardStep.Test, wizard.Step);
        await wizard.TestCommand.ExecuteAsync(null);
        await wizard.GoNextCommand.ExecuteAsync(null);
        await wizard.SaveCommand.ExecuteAsync(null);
        var saved = Assert.Single(configuration.Saved);
        Assert.Equal("/test/bin/" + adapter.LaunchTemplate.Command, saved.StdioCommand);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InstallAgent_UnverifiedInstall_KeepsAdapterStepBlocked(bool installerSucceeded)
    {
        // Arrange
        var probe = new StubExecutableProbe();
        var installer = new StubComponentInstaller(component => installerSucceeded
            ? AcpComponentInstallResult.Success(component.Id, output: null)
            : AcpComponentInstallResult.Failure(component.Id, 1, null, "Install failed"));
        var wizard = CreateWizard(probe, installer);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);
        await wizard.GoNextCommand.ExecuteAsync(null);

        // Act
        wizard.SelectedAgent.RequestInstall();
        await wizard.GoNextCommand.ExecuteAsync(null);

        // Assert
        Assert.Single(installer.InstalledComponentIds);
        Assert.True(wizard.SelectedAgent.IsMissing);
        Assert.False(wizard.GoNextCommand.CanExecute(null));
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
        Assert.Null(wizard.AdapterProbe);
    }

    [Fact]
    public async Task GoNext_RuntimeProbeInFlight_BlocksCommandsAndHonorsCancellation()
    {
        // Arrange
        var pending = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new StubExecutableProbe { ResolveCandidatesAsync = (_, token) => pending.Task.WaitAsync(token) };
        var wizard = CreateWizard(probe);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);

        // Act
        var next = wizard.GoNextCommand.ExecuteAsync(null);
        Assert.True(wizard.IsBusy);
        Assert.False(wizard.GoNextCommand.CanExecute(null));
        Assert.False(wizard.DetectAgentsCommand.CanExecute(null));
        Assert.False(wizard.TestCommand.CanExecute(null));
        wizard.CancelOperationCommand.Execute(null);
        await next.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(wizard.IsBusy);
        Assert.False(wizard.HasErrorMessage);
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
        Assert.Null(wizard.AdapterProbe);
        Assert.True(wizard.GoNextCommand.CanExecute(null));

        probe.ResolveCandidatesAsync = null;
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.True(wizard.SelectedAgent.IsMissing);
        Assert.False(wizard.GoNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task GoNext_RuntimeProbeFails_StaysOnSelectionAndAllowsRetry()
    {
        // Arrange
        var probe = new StubExecutableProbe
        {
            ResolveCandidatesAsync = (_, _) => Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("Probe failed"))
        };
        var wizard = CreateWizard(probe);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);

        // Act
        await wizard.GoNextCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Probe failed", wizard.ErrorMessage);
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
        Assert.Null(wizard.AdapterProbe);
        Assert.True(wizard.GoNextCommand.CanExecute(null));

        probe.ResolveCandidatesAsync = null;
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.False(wizard.HasErrorMessage);
        Assert.True(wizard.SelectedAgent.IsMissing);
    }

    [Fact]
    public async Task GoNext_SelectionChangesDuringRuntimeProbe_DoesNotAdvanceNewSelection()
    {
        // Arrange
        var pending = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new StubExecutableProbe { ResolveCandidatesAsync = (_, _) => pending.Task };
        var wizard = CreateWizard(probe, agents: new[]
        {
            AcpSetupWizardFixtures.Agent("first"),
            AcpSetupWizardFixtures.Agent("second")
        });
        wizard.SelectedAgent = wizard.Agents[0];

        // Act
        var next = wizard.GoNextCommand.ExecuteAsync(null);
        wizard.SelectedAgent = wizard.Agents[1];
        pending.SetResult(new[] { "/test/bin/test-agent" });
        await next.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(wizard.Agents[1], wizard.SelectedAgent);
        Assert.True(wizard.SelectedAgent.IsUndetermined);
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
        Assert.Null(wizard.AdapterProbe);
    }

    [Fact]
    public async Task GoNext_UnrelatedAgents_DoesNotLaunchTheirProbes()
    {
        // Arrange
        var probe = new StubExecutableProbe();
        var wizard = CreateWizard(probe, agents: new[]
        {
            AcpSetupWizardFixtures.Agent("selected"),
            AcpSetupWizardFixtures.Agent("unrelated", runtime: new AcpComponentDescriptor
            {
                Id = "unrelated-cli",
                DisplayName = "Unrelated CLI",
                DetectionMode = AcpComponentDetectionMode.ExecutableOnPath,
                Distribution = AcpDistributionKind.Npx,
                ProbeCommand = "unrelated-command"
            })
        });
        wizard.SelectedAgent = wizard.Agents[0];

        // Act
        await wizard.GoNextCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(new[] { AcpSetupWizardFixtures.RuntimeCommand }, probe.ProbedCommands);
        Assert.True(wizard.Agents[1].IsUndetermined);
    }

    [Fact]
    public async Task GoNext_UnsupportedProcessProbing_PreservesTheUnverifiedSetupPath()
    {
        // Arrange
        var probe = new StubExecutableProbe { SupportsProcessProbing = false };
        var wizard = CreateWizard(probe);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);

        // Act
        await wizard.GoNextCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(AcpSetupWizardStep.Test, wizard.Step);
        Assert.True(wizard.SelectedAgent.IsUndetermined);
        Assert.True(wizard.IsAdapterBuiltIn);
        Assert.True(wizard.SkipTestCommand.CanExecute(null));
        Assert.Empty(probe.ProbedCommands);
    }

    [Fact]
    public async Task GoNext_AgentDisappearedSinceDetection_StaysAtItsInstallAction()
    {
        // Arrange
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/test/bin/test-agent");
        var wizard = CreateWizard(probe);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);
        Assert.True(wizard.SelectedAgent.IsInstalled);
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, null);

        // Act
        await wizard.GoNextCommand.ExecuteAsync(null);

        // Assert
        Assert.True(wizard.SelectedAgent.IsMissing);
        Assert.True(wizard.SelectedAgent.CanInstallHere);
        Assert.False(wizard.GoNextCommand.CanExecute(null));
        Assert.Null(wizard.AdapterProbe);
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
    }

    [Fact]
    public async Task GoNext_CustomPathChangesDuringDetection_DiscardsThePreviousVerdict()
    {
        // Arrange
        var pending = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new StubExecutableProbe { ResolveCandidatesAsync = (_, _) => pending.Task };
        var wizard = CreateWizard(probe);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);

        // Act
        var next = wizard.GoNextCommand.ExecuteAsync(null);
        wizard.SelectedAgent.CustomCommand = "/new/agent";
        pending.SetResult(new[] { "/old/agent" });
        await next.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(wizard.SelectedAgent.IsUndetermined);
        Assert.Equal("/new/agent", wizard.SelectedAgent.CustomCommand);
        Assert.Null(wizard.AdapterProbe);
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
    }

    [Fact]
    public async Task DetectAdapter_PathEditedDuringProbe_DoesNotApproveTheUntestedPath()
    {
        // Arrange
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/test/bin/test-agent");
        var wizard = CreateWizard(
            probe,
            agents: [AcpSetupWizardFixtures.Agent(adapters: AcpSetupWizardFixtures.ExecutableAdapter())]);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.Equal(AcpSetupWizardStep.ComponentSetup, wizard.Step);
        Assert.True(wizard.IsAdapterMissing);
        var pending = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.ResolveCandidatesAsync = (_, token) => pending.Task.WaitAsync(token);
        wizard.AdapterCustomCommand = "/old/adapter";

        // Act
        var detection = wizard.DetectAdapterCommand.ExecuteAsync(null);
        Assert.True(wizard.IsBusy);
        wizard.AdapterCustomCommand = "/new/missing-adapter";
        pending.SetResult(new[] { "/old/adapter" });
        await detection.WaitAsync(TestContext.Current.CancellationToken);
        await wizard.GoNextCommand.ExecuteAsync(null);

        // Assert
        Assert.False(wizard.IsBusy);
        Assert.Equal("/new/missing-adapter", wizard.AdapterCustomCommand);
        Assert.Equal(AcpSetupWizardStep.ComponentSetup, wizard.Step);
        Assert.False(wizard.GoNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task DetectAgents_CancelledSweep_RestoresARetryableSelection()
    {
        // Arrange
        var pending = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new StubExecutableProbe { ResolveCandidatesAsync = (_, token) => pending.Task.WaitAsync(token) };
        var wizard = CreateWizard(
            probe,
            agents: [AcpSetupWizardFixtures.Agent(adapters: AcpSetupWizardFixtures.BuiltInAdapter())]);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);

        // Act
        var detection = wizard.DetectAgentsCommand.ExecuteAsync(null);
        Assert.True(wizard.SelectedAgent.IsChecking);
        wizard.CancelOperationCommand.Execute(null);
        await detection.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(wizard.IsBusy);
        Assert.False(wizard.HasErrorMessage);
        Assert.False(wizard.SelectedAgent.IsChecking);
        Assert.True(wizard.GoNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task GoNext_AgentChangedAwayAndBackDuringProbe_DoesNotConsumeTheOldNextIntent()
    {
        // Arrange
        var pending = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new StubExecutableProbe { ResolveCandidatesAsync = (_, token) => pending.Task.WaitAsync(token) };
        var wizard = AcpSetupWizardFixtures.CreateWizard(
            new StubAgentCatalog(AcpSetupWizardFixtures.Agent("first"), AcpSetupWizardFixtures.Agent("second")),
            probe, new StubComponentInstaller(),
            new StubConnectivityTester(StubConnectivityTester.SuccessfulHandshake()),
            new RecordingConfigurationService());
        wizard.SelectedAgent = wizard.Agents[0];

        // Act
        var next = wizard.GoNextCommand.ExecuteAsync(null);
        wizard.SelectedAgent = wizard.Agents[1];
        wizard.SelectedAgent = wizard.Agents[0];
        pending.SetResult(new[] { "/old/agent" });
        await next.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(wizard.IsBusy);
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
    }

    /// <summary>
    /// An adapter that ships its own copy of the agent must not be blocked on a separate runtime install.
    /// </summary>
    /// <remarks>
    /// The catalog records this for Claude Code and Codex, whose adapter distributions bundle the agent.
    /// Demanding the standalone CLI first tells the user to install something the adapter already carries,
    /// which is the same class of wrong answer as letting an absent runtime through: the gate is asserting
    /// a prerequisite it has not established.
    /// </remarks>
    [Fact]
    public async Task GoNext_AdapterSuppliesItsOwnRuntime_DoesNotRequireASeparateRuntimeInstall()
    {
        // Arrange
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.AdapterCommand, "/test/bin/test-agent-acp");
        var adapter = AcpSetupWizardFixtures.ExecutableAdapter(includesRuntime: true);
        var wizard = CreateWizard(probe, agents: [AcpSetupWizardFixtures.Agent(adapters: adapter)]);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);

        // Act
        await wizard.GoNextCommand.ExecuteAsync(null);

        // Assert
        Assert.True(wizard.SelectedAgent.IsMissing);
        Assert.NotEqual(AcpSetupWizardStep.AgentSelection, wizard.Step);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GoNext_BundledRuntimeMissingAfterDetection_UsesTheRecommendedAdapter(bool selectBeforeDetection)
    {
        // Arrange
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.AdapterCommand, "/test/bin/test-agent-acp");
        var adapter = AcpSetupWizardFixtures.ExecutableAdapter(includesRuntime: true);
        var wizard = CreateWizard(probe, agents: [AcpSetupWizardFixtures.Agent(adapters: adapter)]);
        var row = Assert.Single(wizard.Agents);
        if (selectBeforeDetection)
        {
            wizard.SelectedAgent = row;
        }

        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        wizard.SelectedAgent = row;
        Assert.True(row.IsMissing);

        // Act / Assert: detecting first must not make an otherwise usable adapter unreachable.
        Assert.True(wizard.GoNextCommand.CanExecute(null));
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.Equal(AcpSetupWizardStep.ComponentSetup, wizard.Step);
        Assert.True(wizard.IsAdapterInstalled);
        Assert.True(wizard.GoNextCommand.CanExecute(null));

        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.Equal(AcpSetupWizardStep.Test, wizard.Step);
    }

    [Fact]
    public async Task GoNext_BundledAdapterWithMissingCustomRuntime_RemainsOnSelection()
    {
        // Arrange
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.AdapterCommand, "/test/bin/test-agent-acp");
        var adapter = AcpSetupWizardFixtures.ExecutableAdapter(includesRuntime: true);
        var wizard = CreateWizard(probe, agents: [AcpSetupWizardFixtures.Agent(adapters: adapter)]);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = Assert.Single(wizard.Agents);
        wizard.SelectedAgent = row;
        row.CustomCommand = "/custom/missing-agent";
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        Assert.True(row.IsMissing);
        Assert.Contains(row.CustomCommand, probe.ProbedCommands);
        probe.ProbedCommands.Clear();

        // Act
        await wizard.GoNextCommand.ExecuteAsync(null);

        // Assert: a bundled default must not override the user's explicit runtime choice.
        Assert.False(wizard.GoNextCommand.CanExecute(null));
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
        Assert.Null(wizard.AdapterProbe);
        Assert.Empty(probe.ProbedCommands);
    }

    /// <summary>
    /// Supplying a path for a runtime the wizard reported absent must put the walk back within reach.
    /// </summary>
    /// <remarks>
    /// CanExecute is only re-read when something raises the change notification, so a gate reading a fact
    /// nobody announces keeps showing the verdict it was last told. The custom-path box shares the
    /// selection step with Next and writes on every keystroke, outside any operation whose completion
    /// would re-notify — so a Next left grey here has nothing to correct it, and the user faces a path the
    /// wizard would accept beside a button saying it would not. Asserting through CanExecuteChanged rather
    /// than a bare CanExecute call is the difference that matters: reading the property directly recomputes
    /// it and would pass no matter what the view had been told.
    /// </remarks>
    [Fact]
    public async Task GoNext_CustomPathSuppliedForAMissingRuntime_ReOffersTheWalk()
    {
        // Arrange: nothing on PATH, so the probe positively reports the runtime absent and Next is withheld.
        var wizard = CreateWizard(
            new StubExecutableProbe(),
            agents: [AcpSetupWizardFixtures.Agent(adapters: AcpSetupWizardFixtures.BuiltInAdapter())]);
        var row = Assert.Single(wizard.Agents);
        wizard.SelectedAgent = row;
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
        Assert.True(row.IsMissing);
        Assert.False(wizard.GoNextCommand.CanExecute(null));

        var offerChanged = false;
        wizard.GoNextCommand.CanExecuteChanged += (_, _) => offerChanged = true;

        // Act: exactly what the bound text box writes as the user types.
        row.CustomCommand = "/custom/bin/" + AcpSetupWizardFixtures.RuntimeCommand;

        // Assert
        Assert.False(row.IsMissing);
        Assert.True(offerChanged);
        Assert.True(wizard.GoNextCommand.CanExecute(null));
    }

    /// <summary>
    /// An adapter chosen while an operation holds the wizard busy must still be probed.
    /// </summary>
    /// <remarks>
    /// The adapter list is not disabled during an operation, so the user can re-choose while one runs.
    /// Only one operation is admitted at a time, so the probe the selection asks for cannot start then —
    /// and a request dropped on that floor leaves the new adapter permanently unprobed, which the user
    /// reads as a Next button that never enables.
    /// </remarks>
    [Fact]
    public async Task SelectAdapter_ChosenWhileBusy_IsStillProbed()
    {
        // Arrange: reach the adapter step, then hold the wizard busy on a probe that has not answered.
        var first = AcpSetupWizardFixtures.ExecutableAdapter("adapter.first");
        var second = AcpSetupWizardFixtures.ExecutableAdapter("adapter.second");
        var probe = new StubExecutableProbe();
        probe.SetExecutable(AcpSetupWizardFixtures.RuntimeCommand, "/test/bin/test-agent");
        var wizard = CreateWizard(probe, agents: [AcpSetupWizardFixtures.Agent(adapters: [first, second])]);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.Equal(AcpSetupWizardStep.ComponentSetup, wizard.Step);

        var pending = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.ResolveCandidatesAsync = (_, token) => pending.Task.WaitAsync(token);
        var detection = wizard.DetectAdapterCommand.ExecuteAsync(null);
        Assert.True(wizard.IsBusy);

        // Act: choose the other adapter while busy, then let the displaced operation finish.
        wizard.SelectedAdapter = second;
        probe.ResolveCandidatesAsync = null;
        probe.ProbedCommands.Clear();
        pending.SetResult([]);
        await detection.WaitAsync(TestContext.Current.CancellationToken);
        if (wizard.DetectAdapterCommand.ExecutionTask is { } deferred)
        {
            await deferred.WaitAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        Assert.False(wizard.IsBusy);
        Assert.Same(second, wizard.SelectedAdapter);
        Assert.Contains(AcpSetupWizardFixtures.AdapterCommand, probe.ProbedCommands);
        Assert.NotNull(wizard.AdapterProbe);
    }

    private static AcpSetupWizardViewModel CreateWizard(
        StubExecutableProbe probe,
        StubComponentInstaller? installer = null,
        AcpAgentDescriptor[]? agents = null)
        => AcpSetupWizardFixtures.CreateWizard(
            new StubAgentCatalog(agents ?? new[] { AcpSetupWizardFixtures.Agent() }),
            probe, installer ?? new StubComponentInstaller(),
            new StubConnectivityTester(StubConnectivityTester.SuccessfulHandshake()),
            new RecordingConfigurationService());

    private sealed class StubToolchainInstaller(Action<AcpToolchainRequirement> onInstall) : IAcpToolchainInstaller
    {
        public bool SupportsAutomaticInstall => true;

        public Task<AcpToolchainInstallResult> InstallAsync(
            AcpToolchainRequirement requirement,
            Action<string>? onOutput = null,
            CancellationToken cancellationToken = default)
        {
            onInstall(requirement);
            return Task.FromResult(AcpToolchainInstallResult.Success(
                requirement, "/test/bin", AcpPathRegistration.Applied, output: null));
        }
    }
}
