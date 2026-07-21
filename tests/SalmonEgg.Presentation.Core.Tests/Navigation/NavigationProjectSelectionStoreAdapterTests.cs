using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Services;
using Xunit;
using SalmonEgg.Presentation.Core.Tests.Localization;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

[Collection("NonParallel")]
public sealed class NavigationProjectSelectionStoreAdapterTests
{
    [Fact]
    public void RememberSelectedProject_Unclassified_NormalizesToNull()
    {
        var preferences = CreatePreferences();
        var store = new NavigationProjectSelectionStoreAdapter(preferences);

        store.RememberSelectedProject(MainNavigationViewModel.UnclassifiedProjectId);

        Assert.Null(preferences.LastSelectedProjectId);
    }

    [Fact]
    public void RememberSelectedProject_ProjectId_PersistsExactValue()
    {
        var preferences = CreatePreferences();
        var store = new NavigationProjectSelectionStoreAdapter(preferences);

        store.RememberSelectedProject("project-1");

        Assert.Equal("project-1", preferences.LastSelectedProjectId);
    }

    private static AppPreferencesViewModel CreatePreferences()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        return new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            prefsLogger.Object,
            new ImmediateUiDispatcher());
    }
}
