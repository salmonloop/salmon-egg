using System.Threading.Tasks;
using SalmonEgg.Application.Services.AcpSetup;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;

namespace SalmonEgg.Presentation.Core.Tests.Settings.AcpSetup;

public sealed class AcpSetupSaveValidationTests
{
    [Fact]
    public async Task SkipThenSave_UnsupportedRuntimeLauncher_ShowsLocalizedRemediationAndAllowsCorrection()
    {
        // Arrange: the launcher exists, but this adapter starts runtimes without a Windows shell.
        const string unsupportedRuntime = @"C:\Tools\test-agent.cmd";
        const string supportedRuntime = @"C:\Tools\test-agent.exe";
        const string remediation = "请选择原生可执行文件，或清空路径以使用适配器自带的运行时。";
        var probe = new StubExecutableProbe();
        probe.SetExecutable(unsupportedRuntime, unsupportedRuntime);
        probe.SetExecutable(supportedRuntime, supportedRuntime);
        probe.SetExecutable(AcpSetupWizardFixtures.AdapterCommand, "/test/bin/test-agent-acp");
        probe.SetExecutable("/test/bin/test-agent-acp", "/test/bin/test-agent-acp");
        var executableAdapter = AcpSetupWizardFixtures.ExecutableAdapter();
        var adapter = new AcpAdapterDescriptor
        {
            Component = executableAdapter.Component,
            LaunchTemplate = executableAdapter.LaunchTemplate,
            IncludesRuntime = true,
            RuntimeCommandEnvironmentVariable = "TEST_AGENT_RUNTIME",
            SupportsWindowsRuntimeBatchLauncher = false
        };
        var tester = new StubConnectivityTester(StubConnectivityTester.SuccessfulHandshake());
        var configuration = new RecordingConfigurationService();
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", AcpSetupParameterValidator.RuntimeBatchLauncherNotSupportedKey, remediation);
        var wizard = AcpSetupWizardFixtures.CreateWizard(
            new StubAgentCatalog(AcpSetupWizardFixtures.Agent(adapters: adapter)),
            probe, new StubComponentInstaller(), tester, configuration, localizer);
        await wizard.DetectAgentsCommand.ExecuteAsync(null);
        var row = Assert.Single(wizard.Agents);
        wizard.SelectedAgent = row;
        row.CustomCommand = unsupportedRuntime;
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.Equal(AcpSetupWizardStep.ComponentSetup, wizard.Step);
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.Equal(AcpSetupWizardStep.Test, wizard.Step);
        Assert.True(wizard.SkipTestCommand.CanExecute(null));
        wizard.SkipTestCommand.Execute(null);
        Assert.Equal(AcpSetupWizardStep.Save, wizard.Step);
        Assert.True(wizard.SaveCommand.CanExecute(null));

        // Act
        await wizard.SaveCommand.ExecuteAsync(null);

        // Assert: skipping connectivity testing does not bypass an established launch constraint.
        Assert.Empty(configuration.Saved);
        Assert.Null(wizard.SavedProfile);
        Assert.Equal(AcpSetupWizardStep.Test, wizard.Step);
        Assert.Equal(ProfileVerificationState.Unknown, wizard.Verification.State);
        Assert.Equal(AcpSetupTestStage.Validation, wizard.TestResult?.Stage);
        Assert.Equal(remediation, wizard.TestRemediationText);
        Assert.False(wizard.HasErrorMessage);
        Assert.False(wizard.SaveCommand.CanExecute(null));
        Assert.Equal(0, tester.TestCount);

        // The user can return to the path field, correct it, and explicitly save an unverified profile.
        wizard.GoBackCommand.Execute(null);
        Assert.Equal(AcpSetupWizardStep.ComponentSetup, wizard.Step);
        wizard.GoBackCommand.Execute(null);
        Assert.Equal(AcpSetupWizardStep.AgentSelection, wizard.Step);
        row.CustomCommand = supportedRuntime;
        await wizard.GoNextCommand.ExecuteAsync(null);
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.Equal(AcpSetupWizardStep.Test, wizard.Step);
        wizard.SkipTestCommand.Execute(null);
        await wizard.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(configuration.Saved);
        Assert.Same(saved, wizard.SavedProfile);
        Assert.Equal(ProfileVerificationState.Unverified, saved.Verification.State);
        Assert.Equal(supportedRuntime, saved.StdioEnvironment[adapter.RuntimeCommandEnvironmentVariable]);
        Assert.Equal(0, tester.TestCount);
    }
}
