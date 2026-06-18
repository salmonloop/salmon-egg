using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
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
    public async Task LoadAsync_RestoresAnimationPreference()
    {
        var appSettings = new AppSettings
        {
            Theme = "System",
            IsAnimationEnabled = false,
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

        await Task.Delay(100);

        Assert.False(vm.IsAnimationEnabled);
        uiRuntime.Verify(u => u.SetAnimationsEnabled(false), Times.AtLeastOnce);
        appSettingsService.Verify(s => s.SaveAsync(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task LoadAsync_NormalizesLegacyLanguageTagBeforeApplyingOverride()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings
        {
            Language = "zh-CN"
        });

        var startupService = new Mock<IAppStartupService>();
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await Task.Delay(100);

        Assert.Equal("zh-Hans", vm.Language);
        languageService.Verify(service => service.ApplyLanguageOverrideAsync("zh-Hans"), Times.Once);
    }

    [Fact]
    public async Task LanguageChanged_NormalizesBeforeSavingAndReloadingShell()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var startupService = new Mock<IAppStartupService>();
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await Task.Delay(100);
        languageService.Invocations.Clear();
        uiRuntime.Invocations.Clear();

        vm.Language = "zh-CN";

        await Task.Delay(1200);

        Assert.Equal("zh-Hans", vm.Language);
        languageService.Verify(service => service.ApplyLanguageOverrideAsync("zh-Hans"), Times.Once);
        uiRuntime.Verify(service => service.ReloadShell(), Times.Once);
        appSettingsService.Verify(
            service => service.SaveAsync(It.Is<AppSettings>(settings => settings.Language == "zh-Hans")),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task LoadAsync_RestoresAgentRemoteDirectories()
    {
        var settingsService = new FakeAppSettingsService(new AppSettings
        {
            AgentRemoteDirectories = new List<AgentRemoteDirectory>
            {
                new() { DirectoryId = "dir-a", DisplayName = "Alpha", RemotePath = "/remote/alpha" }
            }
        });
        var vm = CreateViewModel(settingsService);

        await settingsService.LoadCompletion;

        var directory = Assert.Single(vm.AgentRemoteDirectories);
        Assert.Equal("dir-a", directory.DirectoryId);
        Assert.Equal("Alpha", directory.DisplayName);
        Assert.Equal("/remote/alpha", directory.RemotePath);
    }

    [Fact]
    public async Task ScheduleSave_PersistsNormalizedAgentRemoteDirectories()
    {
        var settingsService = new FakeAppSettingsService(new AppSettings());
        var vm = CreateViewModel(settingsService);
        await settingsService.LoadCompletion;

        vm.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = " dir ",
            DisplayName = " Workspace ",
            RemotePath = " /remote/workspace "
        });

        await WaitForConditionAsync(() =>
            settingsService.LastSaved?.AgentRemoteDirectories.Count == 1
            && settingsService.LastSaved.AgentRemoteDirectories[0].DirectoryId == "dir"
            && settingsService.LastSaved.AgentRemoteDirectories[0].DisplayName == "Workspace"
            && settingsService.LastSaved.AgentRemoteDirectories[0].RemotePath == "/remote/workspace");
    }

    [Fact]
    public async Task LoadAsync_CollapsesLegacyProfileBoundRemoteDirectoriesByRemotePath()
    {
        var settingsService = new FakeAppSettingsService(new AppSettings
        {
            AgentRemoteDirectories = new List<AgentRemoteDirectory>
            {
                new() { DirectoryId = "dir-a", DisplayName = "Alpha", RemotePath = "/remote/shared" },
                new() { DirectoryId = "dir-b", DisplayName = "Beta", RemotePath = "/remote/shared" },
                new() { DirectoryId = "dir-c", DisplayName = "Gamma", RemotePath = "/remote/other" }
            }
        });
        var vm = CreateViewModel(settingsService);
        await settingsService.LoadCompletion;

        Assert.Collection(
            vm.AgentRemoteDirectories,
            first =>
            {
                Assert.Equal("dir-b", first.DirectoryId);
                Assert.Equal("Beta", first.DisplayName);
                Assert.Equal("/remote/shared", first.RemotePath);
            },
            second =>
            {
                Assert.Equal("dir-c", second.DirectoryId);
                Assert.Equal("Gamma", second.DisplayName);
                Assert.Equal("/remote/other", second.RemotePath);
            });
    }

    [Fact]
    public async Task AcpEnabledChanged_PersistsGlobalAcpPolicy()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings
        {
            AcpEnabled = true
        });
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await Task.Delay(100);

        vm.AcpEnabled = false;

        await Task.Delay(1200);

        appSettingsService.Verify(
            service => service.SaveAsync(It.Is<AppSettings>(settings => settings.AcpEnabled == false)),
            Times.AtLeastOnce);
    }

    private static AppPreferencesViewModel CreateViewModel(FakeAppSettingsService settingsService)
    {
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var logger = new Mock<ILogger<AppPreferencesViewModel>>();
        return new AppPreferencesViewModel(
            settingsService,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            logger.Object,
            new ImmediateUiDispatcher());
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, int timeoutMilliseconds = 5000, int pollDelayMilliseconds = 20)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(pollDelayMilliseconds).ConfigureAwait(false);
        }

        Assert.True(predicate(), "Condition was not satisfied within the allotted time.");
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        private readonly AppSettings _settings;
        private readonly TaskCompletionSource _loadTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeAppSettingsService(AppSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public Task LoadCompletion => _loadTcs.Task;

        public AppSettings? LastSaved { get; private set; }

        public Task<AppSettings> LoadAsync()
        {
            var result = _settings;
            _loadTcs.TrySetResult();
            return Task.FromResult(result);
        }

        public Task SaveAsync(AppSettings settings)
        {
            LastSaved = settings;
            return Task.CompletedTask;
        }
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

    [Fact]
    public async Task SetKeyBinding_ExistingBinding_RaisesShortcutBindingsChanged()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings
        {
            KeyBindings = new Dictionary<string, string>
            {
                ["search"] = "Ctrl+K"
            }
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

        var raisedCount = 0;
        vm.ShortcutBindingsChanged += (_, _) => raisedCount++;

        vm.SetKeyBinding("search", "Alt+K");

        Assert.Equal("Alt+K", vm.GetKeyBinding("search"));
        Assert.Equal(1, raisedCount);
    }
}
