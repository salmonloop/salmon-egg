using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class ConfigProjectionReloadCoordinatorTests
{
    [Fact]
    public async Task RestoredConfig_ReloadsProjectionOwnersInSsotOrder()
    {
        var configRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "salmon-config", Guid.NewGuid().ToString("N"));
        var signal = new FakeConfigChangeSignal();
        var appData = new FakeAppDataService(configRoot);
        var settingsService = new FakeAppSettingsService(new AppSettings { Theme = "Light", Language = "en-US" });
        var preferences = CreatePreferences(settingsService);
        await preferences.InitializeAsync(TestContext.Current.CancellationToken);
        settingsService.Settings = new AppSettings
        {
            Theme = "Dark",
            Language = "zh-Hans",
            LastSelectedServerId = "profile-b"
        };

        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.ListConfigurationsAsync())
            .ReturnsAsync([
                new ServerConfiguration
                {
                    Id = "profile-b",
                    Name = "Profile B",
                    ServerUrl = "wss://example.test/acp"
                }
            ]);
        var profiles = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            Mock.Of<ILogger<AcpProfilesViewModel>>(),
            new ImmediateUiDispatcher());

        var mcpSettings = new FakeMcpSettingsService
        {
            Settings = new McpSettings
            {
                Servers =
                {
                    new McpServerCatalogEntry(new StdioMcpServer("tools", "tool-server"))
                }
            }
        };
        var mcp = new McpSettingsViewModel(
            mcpSettings,
            Mock.Of<IPlatformShellService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<McpSettingsViewModel>>(),
            new ImmediateUiDispatcher());

        using var coordinator = new ConfigProjectionReloadCoordinator(
            signal,
            appData,
            preferences,
            profiles,
            mcp,
            Mock.Of<ILogger<ConfigProjectionReloadCoordinator>>());

        signal.NotifyChanged(configRoot, ConfigChangeKind.Restored);
        await mcpSettings.LoadCompletion.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Dark", preferences.Theme);
        Assert.Equal("zh-Hans", preferences.Language);
        Assert.Equal("profile-b", profiles.SelectedProfileId);
        Assert.Single(profiles.Profiles);
        Assert.Single(mcp.Servers);
        Assert.Equal("tools", mcp.Servers[0].Name);
    }

    private static AppPreferencesViewModel CreatePreferences(IAppSettingsService settingsService)
    {
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(service => service.IsSupported).Returns(false);
        startupService.Setup(service => service.GetLaunchOnStartupAsync()).ReturnsAsync((bool?)null);
        var languageService = new Mock<IAppLanguageService>();
        languageService
            .Setup(service => service.ApplyLanguageOverrideAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        return new AppPreferencesViewModel(
            settingsService,
            startupService.Object,
            languageService.Object,
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());
    }

    private sealed class FakeConfigChangeSignal : IConfigChangeSignal
    {
        private int _suppressCount;

        public event EventHandler<ConfigChangedEventArgs>? Changed;

        public bool IsSuppressed => _suppressCount > 0;

        public IDisposable Suppress()
        {
            _suppressCount++;
            return new Scope(() => _suppressCount--);
        }

        public void NotifyChanged(string path, ConfigChangeKind kind)
        {
            if (!IsSuppressed)
            {
                Changed?.Invoke(this, new ConfigChangedEventArgs(path, kind));
            }
        }
    }

    private sealed class FakeAppDataService(string configRoot) : IAppDataService
    {
        public string AppDataRootPath { get; } = System.IO.Path.GetDirectoryName(configRoot) ?? configRoot;

        public string ConfigRootPath { get; } = configRoot;

        public string LogsDirectoryPath => System.IO.Path.Combine(AppDataRootPath, "logs");

        public string CacheRootPath => System.IO.Path.Combine(AppDataRootPath, "cache");

        public string ExportsDirectoryPath => System.IO.Path.Combine(AppDataRootPath, "exports");
    }

    private sealed class FakeAppSettingsService(AppSettings settings) : IAppSettingsService
    {
        public AppSettings Settings { get; set; } = settings;

        public Task<AppSettings> LoadAsync() => Task.FromResult(Settings);

        public Task SaveAsync(AppSettings settings)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMcpSettingsService : IMcpSettingsService
    {
        private readonly TaskCompletionSource _loadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public McpSettings Settings { get; set; } = new();

        public Task LoadCompletion => _loadCompletion.Task;

        public Task<McpSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            _loadCompletion.TrySetResult();
            return Task.FromResult(Settings);
        }

        public Task SaveAsync(McpSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
