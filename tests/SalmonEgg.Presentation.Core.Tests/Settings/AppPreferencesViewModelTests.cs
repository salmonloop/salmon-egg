using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public class AppPreferencesViewModelTests
{
    [Fact]
    public async Task IsAnimationEnabled_Changes_InvokeUiRuntimeService()
    {
        var appSettings = new AppSettings
        {
            Theme = "System",
            IsAnimationEnabled = true,
            Backdrop = "System",
            LaunchOnStartup = false,
            MinimizeToTray = true,
            Language = "System",
            SaveLocalHistory = true,
            HistoryRetentionDays = 30,
            RememberRecentProjectPaths = true,
            CacheRetentionDays = 7
        };

        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(appSettings);

        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);

        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(c => c.SupportsLaunchOnStartup).Returns(false);
        capabilities.SetupGet(c => c.SupportsTray).Returns(false);
        capabilities.SetupGet(c => c.SupportsLanguageOverride).Returns(false);

        var uiRuntime = new Mock<IUiRuntimeService>();
        var logger = new Mock<ILogger<AppPreferencesViewModel>>();

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            logger.Object);

        await Task.Delay(10);
        uiRuntime.Invocations.Clear();

        vm.IsAnimationEnabled = false;

        uiRuntime.Verify(u => u.SetAnimationsEnabled(false), Times.Once);
    }
}
