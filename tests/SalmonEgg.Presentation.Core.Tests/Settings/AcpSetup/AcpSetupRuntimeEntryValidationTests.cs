using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;
using SalmonEgg.Application.Services.AcpSetup;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;

namespace SalmonEgg.Presentation.Core.Tests.Settings.AcpSetup;

public sealed class AcpSetupRuntimeEntryValidationTests
{
    private const string NativeExecutableMessage = "此适配器不能使用 Windows 批处理文件作为 Agent 运行时。请选择原生可执行程序（例如 claude.exe），或清空自定义运行时路径以使用适配器自带的运行时。";

    [Fact]
    public async Task Test_InvalidWindowsRuntime_ClearsVerificationWithoutStartingConnectivityTest()
    {
        // Arrange
        var (wizard, tester, _) = await CreateWizardAsync();
        wizard.Verification = ProfileVerification.Verified(DateTimeOffset.UtcNow);

        // Act
        await wizard.TestCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(0, tester.TestCount);
        Assert.Equal(ProfileVerification.Unknown, wizard.Verification);
        Assert.Equal(AcpSetupTestStage.Validation, wizard.TestResult?.Stage);
        Assert.Equal(NativeExecutableMessage, wizard.TestRemediationText);
        Assert.Equal(@"C:\Agent\claude.cmd", wizard.SelectedAgent?.CustomCommand);
    }

    [Fact]
    public async Task Save_InvalidWindowsRuntime_ReportsValidationAndClearsClaimedVerification()
    {
        // Arrange
        var (wizard, tester, configuration) = await CreateWizardAsync();
        wizard.SkipTestCommand.Execute(null);
        Assert.Equal(AcpSetupWizardStep.Save, wizard.Step);
        wizard.Verification = ProfileVerification.Verified(DateTimeOffset.UtcNow);

        // Act
        await wizard.SaveCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(configuration.Saved);
        Assert.Null(wizard.SavedProfile);
        Assert.Equal(0, tester.TestCount);
        Assert.Equal(AcpSetupWizardStep.Test, wizard.Step);
        Assert.Equal(ProfileVerification.Unknown, wizard.Verification);
        Assert.Equal(AcpSetupTestStage.Validation, wizard.TestResult?.Stage);
        Assert.Equal(NativeExecutableMessage, wizard.TestRemediationText);
        Assert.False(wizard.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("zh-Hans")]
    [InlineData("en")]
    [InlineData("en-US")]
    public void RuntimeEntryValidation_Remediation_UsesPackagedLanguageResource(string language)
    {
        // Arrange
        var resources = new ResourceManager(typeof(CoreStrings));

        // Act
        var message = resources.GetString(AcpSetupParameterValidator.RuntimeBatchLauncherNotSupportedKey,
            CultureInfo.GetCultureInfo(language));

        // Assert
        Assert.Equal(language.StartsWith("en", StringComparison.Ordinal)
            ? "This adapter cannot use a Windows batch file as its agent runtime. Choose the native executable (such as claude.exe), or clear the custom runtime path to use the adapter's bundled runtime."
            : NativeExecutableMessage, message);
    }

    private static async Task<(AcpSetupWizardViewModel Wizard, StubConnectivityTester Tester, RecordingConfigurationService Configuration)> CreateWizardAsync()
    {
        var adapter = new AcpAdapterDescriptor
        {
            Component = new AcpComponentDescriptor
            {
                Id = "adapter",
                DisplayName = "Adapter",
                Distribution = AcpDistributionKind.Binary,
                DetectionMode = AcpComponentDetectionMode.ExecutableOnPath,
                ProbeCommand = "adapter"
            },
            LaunchTemplate = new AcpLaunchTemplate { Command = "adapter" },
            IncludesRuntime = true,
            RuntimeCommandEnvironmentVariable = "CLAUDE_CODE_EXECUTABLE",
            SupportsWindowsRuntimeBatchLauncher = false
        };
        var probe = new StubExecutableProbe();
        probe.SetExecutable(@"C:\Agent\claude.cmd", @"C:\Agent\claude.cmd");
        probe.SetExecutable("adapter", @"C:\Agent\adapter.cmd");
        var tester = new StubConnectivityTester(StubConnectivityTester.SuccessfulHandshake());
        var configuration = new RecordingConfigurationService();
        var localizer = new MockResourceLocalizer();
        var wizard = AcpSetupWizardFixtures.CreateWizard(
            new StubAgentCatalog(AcpSetupWizardFixtures.Agent(adapters: adapter)), probe,
            new StubComponentInstaller(), tester, configuration, localizer: localizer);
        wizard.SelectedAgent = Assert.Single(wizard.Agents);
        wizard.SelectedAgent.CustomCommand = @"C:\Agent\claude.cmd";
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.Equal(AcpSetupWizardStep.ComponentSetup, wizard.Step);
        await wizard.GoNextCommand.ExecuteAsync(null);
        Assert.Equal(AcpSetupWizardStep.Test, wizard.Step);
        return (wizard, tester, configuration);
    }

    private sealed class MockResourceLocalizer : IStringLocalizer<CoreStrings>
    {
        private static readonly ResourceManager Resources = new(typeof(CoreStrings));

        public LocalizedString this[string name] => new(name,
            Resources.GetString(name, CultureInfo.GetCultureInfo("zh-Hans")) ?? name);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
