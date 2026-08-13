using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public class AppPreferencesViewModelTests
{
    [Fact]
    public void Constructor_DoesNotLoadAppSettings()
    {
        var appSettingsService = new Mock<IAppSettingsService>();

        _ = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        appSettingsService.Verify(service => service.LoadAsync(), Times.Never);
    }

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
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            logger.Object,
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);
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
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            logger.Object,
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

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
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("zh-Hans", vm.Language);
        Assert.NotNull(vm.SelectedLanguageOption);
        Assert.Equal("zh-Hans", vm.SelectedLanguageOption.Tag);
        languageService.Verify(service => service.ApplyLanguageOverrideAsync("zh-Hans"), Times.Once);
    }

    [Fact]
    public async Task LanguageChanged_UpdatesSelectedLanguageOption()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        vm.Language = "en-US";

        Assert.NotNull(vm.SelectedLanguageOption);
        Assert.Equal("en-US", vm.SelectedLanguageOption.Tag);
    }

    [Fact]
    public async Task SelectedLanguageOptionChanged_UpdatesLanguage()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        vm.SelectedLanguageOption = vm.LanguageOptions.Single(option => option.Tag == "en-US");

        Assert.Equal("en-US", vm.Language);
    }

    [Fact]
    public async Task AppearanceOptionChanges_UpdatePreferenceValues()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        vm.SelectedThemeOption = vm.ThemeOptions.Single(option => option.Value == "Dark");
        vm.SelectedBackdropOption = vm.BackdropOptions.Single(option => option.Value == "Acrylic");

        Assert.Equal("Dark", vm.Theme);
        Assert.Equal("Acrylic", vm.Backdrop);
    }

    [Fact]
    public async Task PreferenceValueChanges_UpdateAppearanceOptions()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        vm.Theme = "Light";
        vm.Backdrop = "Mica";

        Assert.NotNull(vm.SelectedThemeOption);
        Assert.NotNull(vm.SelectedBackdropOption);
        Assert.Equal("Light", vm.SelectedThemeOption.Value);
        Assert.Equal("Mica", vm.SelectedBackdropOption.Value);
    }

    [Fact]
    public async Task LanguageChanged_NormalizesBeforeSavingAndReloadingShell()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        // 捕获最后一次持久化的语言：debounce 的保存在后台线程触发，直接枚举 Moq.Invocations 会
        // 与并发写竞态，故用回调投影到线程安全 holder（数组元素可作 Volatile 的 ref 目标）供轮询。
        var lastSavedLanguage = new string?[1];
        appSettingsService
            .Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Callback<AppSettings>(settings => Volatile.Write(ref lastSavedLanguage[0], settings.Language))
            .Returns(Task.CompletedTask);

        var startupService = new Mock<IAppStartupService>();
        var languageService = new Mock<IAppLanguageService>();
        languageService
            .Setup(s => s.ApplyLanguageOverrideAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);
        languageService.Invocations.Clear();
        uiRuntime.Invocations.Clear();
        Volatile.Write(ref lastSavedLanguage[0], null);

        vm.Language = "zh-CN";

        // 语言变更经 debounce 后才落副作用；持久化是最后一步，轮询到它落盘即保证整条链完成，
        // 避免固定 sleep 的时序脆弱，也避免抢在 debounce 保存之前断言。
        await WaitForConditionAsync(() =>
            string.Equals(Volatile.Read(ref lastSavedLanguage[0]), "zh-Hans", StringComparison.Ordinal));

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

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

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
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

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
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

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
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
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

        public event EventHandler<AppSettingsSavedEventArgs>? Saved;

        public Task<AppSettings> LoadAsync()
        {
            var result = _settings;
            _loadTcs.TrySetResult();
            return Task.FromResult(result);
        }

        public Task SaveAsync(AppSettings settings)
        {
            LastSaved = settings;
            Saved?.Invoke(this, new AppSettingsSavedEventArgs(settings));
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
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            logger.Object,
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        vm.ResetToDefaults();

        Assert.Equal("project-123", vm.LastSelectedProjectId);
    }

    [Fact]
    public async Task SetKeyBinding_ExistingBinding_RaisesShortcutConfigurationChanged()
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
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            logger.Object,
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        var raisedCount = 0;
        vm.ShortcutConfigurationChanged += (_, _) => raisedCount++;

        vm.SetKeyBinding("search", "Alt+K");

        Assert.Equal("Alt+K", vm.GetKeyBinding("search"));
        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public async Task LoadAsync_RestoresKeyboardShortcutsEnabled()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings
        {
            KeyboardShortcutsEnabled = false
        });

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.KeyboardShortcutsEnabled);
    }

    [Fact]
    public async Task KeyboardShortcutsEnabledChanged_PersistsGlobalShortcutPolicy()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings
        {
            KeyboardShortcutsEnabled = true
        });
        // debounce 的持久化在后台线程触发；用 TaskCompletionSource 等待首个「关闭」落盘，
        // 避免固定 sleep 的时序脆弱，也避免并发枚举 Moq.Invocations。
        var shortcutsDisabledSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        appSettingsService
            .Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Returns(Task.CompletedTask)
            .Callback<AppSettings>(settings =>
            {
                if (!settings.KeyboardShortcutsEnabled)
                {
                    shortcutsDisabledSaved.TrySetResult();
                }
            });

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        vm.KeyboardShortcutsEnabled = false;

        await shortcutsDisabledSaved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        appSettingsService.Verify(
            service => service.SaveAsync(It.Is<AppSettings>(settings => settings.KeyboardShortcutsEnabled == false)),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task LaunchOnStartup_WhenOsApplyDenied_RevertsAndSurfacesInfo()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings
        {
            LaunchOnStartup = false
        });
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(true);
        startupService.Setup(s => s.GetLaunchOnStartupAsync()).ReturnsAsync(false);
        startupService.Setup(s => s.SetLaunchOnStartupAsync(true)).ReturnsAsync(false);

        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(c => c.SupportsLaunchOnStartup).Returns(true);

        var ui = new Mock<IUiInteractionService>();
        ui.Setup(s => s.ShowInfoAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            Mock.Of<IAppLanguageService>(),
            capabilities.Object,
            Mock.Of<IUiRuntimeService>(),
            ui.Object,
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(vm.LaunchOnStartup);

        vm.LaunchOnStartup = true;

        // ApplyLaunchOnStartupAsync is fire-and-forget; wait for revert + dialog.
        for (var i = 0; i < 50 && vm.LaunchOnStartup; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.False(vm.LaunchOnStartup);
        ui.Verify(
            s => s.ShowInfoAsync("Failed to update launch on startup. Please try again later."),
            Times.Once);
        startupService.Verify(s => s.SetLaunchOnStartupAsync(true), Times.Once);
        startupService.Verify(s => s.SetLaunchOnStartupAsync(false), Times.Never);
    }

    [Fact]
    public async Task LaunchOnStartup_WhenOsApplyThrows_RevertsAndSurfacesInfo()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings
        {
            LaunchOnStartup = false
        });
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(true);
        startupService.Setup(s => s.GetLaunchOnStartupAsync()).ReturnsAsync(false);
        startupService.Setup(s => s.SetLaunchOnStartupAsync(true))
            .ThrowsAsync(new InvalidOperationException("denied"));

        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(c => c.SupportsLaunchOnStartup).Returns(true);

        var ui = new Mock<IUiInteractionService>();
        ui.Setup(s => s.ShowInfoAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            Mock.Of<IAppLanguageService>(),
            capabilities.Object,
            Mock.Of<IUiRuntimeService>(),
            ui.Object,
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);
        vm.LaunchOnStartup = true;

        for (var i = 0; i < 50 && vm.LaunchOnStartup; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.False(vm.LaunchOnStartup);
        ui.Verify(
            s => s.ShowInfoAsync("Failed to update launch on startup. Please try again later."),
            Times.Once);
    }

    [Fact]
    public async Task LanguageChanged_WhenApplyFails_RevertsAndSurfacesInfo()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings
        {
            Language = "en-US"
        });
        appSettingsService.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);

        var languageService = new Mock<IAppLanguageService>();
        languageService
            .Setup(s => s.ApplyLanguageOverrideAsync("en-US"))
            .Returns(Task.CompletedTask);
        languageService
            .Setup(s => s.ApplyLanguageOverrideAsync("zh-Hans"))
            .ThrowsAsync(new InvalidOperationException("apply failed"));

        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(c => c.SupportsLanguageOverride).Returns(true);

        var ui = new Mock<IUiInteractionService>();
        ui.Setup(s => s.ShowInfoAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var uiRuntime = new Mock<IUiRuntimeService>();

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            ui.Object,
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal("en-US", vm.Language);
        languageService.Invocations.Clear();
        uiRuntime.Invocations.Clear();

        vm.Language = "zh-Hans";

        for (var i = 0; i < 50 && string.Equals(vm.Language, "zh-Hans", StringComparison.Ordinal); i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal("en-US", vm.Language);
        Assert.Equal("en-US", vm.SelectedLanguageOption?.Tag);
        ui.Verify(
            s => s.ShowInfoAsync("Failed to change language. Please try again later."),
            Times.Once);
        uiRuntime.Verify(s => s.ReloadShell(), Times.Never);
        languageService.Verify(s => s.ApplyLanguageOverrideAsync("zh-Hans"), Times.Once);
        languageService.Verify(s => s.ApplyLanguageOverrideAsync("en-US"), Times.Never);
    }

    [Fact]
    public async Task ScheduleSave_WhenSaveFails_SurfacesInfo()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        appSettingsService
            .Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .ThrowsAsync(new InvalidOperationException("disk full"));

        var ui = new Mock<IUiInteractionService>();
        ui.Setup(s => s.ShowInfoAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            ui.Object,
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);
        ui.Invocations.Clear();

        vm.IsAnimationEnabled = !vm.IsAnimationEnabled;

        for (var i = 0; i < 80; i++)
        {
            if (ui.Invocations.Count > 0)
            {
                break;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        ui.Verify(
            s => s.ShowInfoAsync("Failed to save app settings. Please try again later."),
            Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_WhenLoadFails_SurfacesInfo()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService
            .Setup(s => s.LoadAsync())
            .ThrowsAsync(new InvalidOperationException("corrupt settings"));

        var ui = new Mock<IUiInteractionService>();
        ui.Setup(s => s.ShowInfoAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new AppPreferencesViewModel(
            appSettingsService.Object,
            Mock.Of<IAppStartupService>(),
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            ui.Object,
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());

        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        ui.Verify(
            s => s.ShowInfoAsync("Failed to load app settings. Please try again later."),
            Times.Once);
        Assert.True(vm.IsLoaded);
    }

}
