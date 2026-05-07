using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
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
            logger.Object,
            new ImmediateUiDispatcher());

        await Task.Delay(10);
        uiRuntime.Invocations.Clear();

        vm.IsAnimationEnabled = false;

        uiRuntime.Verify(u => u.SetAnimationsEnabled(false), Times.Once);
    }

    [Fact]
    public async Task LoadAsync_RestoresProjectPathMappings()
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
            CacheRetentionDays = 7,
            ProjectPathMappings = new List<ProjectPathMapping>
            {
                new()
                {
                    ProfileId = "profile-one",
                    RemoteRootPath = "/remote/one",
                    LocalRootPath = "C:\\Project\\One"
                },
                new()
                {
                    ProfileId = " profile-two ",
                    RemoteRootPath = " /remote/two ",
                    LocalRootPath = " C:\\Project\\Two "
                }
            }
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
            logger.Object,
            new ImmediateUiDispatcher());

        await Task.Delay(100);

        Assert.Collection(
            vm.ProjectPathMappings,
            first =>
            {
                Assert.Equal("profile-one", first.ProfileId);
                Assert.Equal("/remote/one", first.RemoteRootPath);
                Assert.Equal("C:\\Project\\One", first.LocalRootPath);
            },
            second =>
            {
                Assert.Equal("profile-two", second.ProfileId);
                Assert.Equal("/remote/two", second.RemoteRootPath);
                Assert.Equal("C:\\Project\\Two", second.LocalRootPath);
            });
    }

    [Fact]
    public async Task ScheduleSave_PersistsNormalizedProjectPathMappings()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings
        {
            Theme = "System",
            IsAnimationEnabled = true,
            Backdrop = "System",
            LaunchOnStartup = false,
            MinimizeToTray = true,
            Language = "System",
            SaveLocalHistory = true,
            CacheRetentionDays = 7
        });
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

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
            logger.Object,
            new ImmediateUiDispatcher());

        await Task.Delay(100);

        vm.ProjectPathMappings.Add(new ProjectPathMapping
        {
            ProfileId = " profile ",
            RemoteRootPath = " /remote ",
            LocalRootPath = " local "
        });

        await Task.Delay(1200);

        appSettingsService.Verify(
            s => s.SaveAsync(It.Is<AppSettings>(saved =>
                saved.ProjectPathMappings.Count == 1
                && saved.ProjectPathMappings[0].ProfileId == "profile"
                && saved.ProjectPathMappings[0].RemoteRootPath == "/remote"
                && saved.ProjectPathMappings[0].LocalRootPath == "local")),
            Times.AtLeastOnce);
    }

    [Fact]
    public void RemovedStoragePreferenceProperties_AreNotExposed()
    {
        Assert.Null(typeof(AppPreferencesViewModel).GetProperty("HistoryRetentionDays"));
        Assert.Null(typeof(AppPreferencesViewModel).GetProperty("RememberRecentProjectPaths"));
        Assert.Null(typeof(AppSettings).GetProperty("HistoryRetentionDays"));
        Assert.Null(typeof(AppSettings).GetProperty("RememberRecentProjectPaths"));
    }

    [Fact]
    public async Task ResetToDefaults_PreservesLastSelectedProjectId()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings
        {
            LastSelectedProjectId = "project-123"
        });
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

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
            logger.Object,
            new ImmediateUiDispatcher());

        await Task.Delay(100);

        vm.ResetToDefaults();

        Assert.Equal("project-123", vm.LastSelectedProjectId);
    }
}
