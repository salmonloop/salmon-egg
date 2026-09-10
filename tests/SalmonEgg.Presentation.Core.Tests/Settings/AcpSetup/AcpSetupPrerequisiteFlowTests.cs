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
