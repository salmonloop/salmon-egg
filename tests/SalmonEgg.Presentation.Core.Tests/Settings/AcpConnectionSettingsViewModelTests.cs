using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class AcpConnectionSettingsViewModelTests
{
    [Fact]
    public async Task Constructor_ComposesNarrowAcpContracts_WithoutFullChatSemantics()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var transport = new TestTransportConfiguration();
        var state = new TestConnectionState { AgentName = "Adapter Agent" };
        var commands = new TestConnectionCommands();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(
            state,
            commands,
            transport,
            profiles,
            preferences,
            CreateTransportSupportPolicy(preferences),
            logger.Object,
            new TestCoreStringLocalizer());

        Assert.IsAssignableFrom<ISettingsChatConnection>(viewModel.Chat);

        var profile = new ServerConfiguration { Id = "profile-1", Name = "Profile Alpha" };
        await viewModel.ConnectToProfileAsync(profile);

        Assert.Same(profile, commands.ConnectedProfiles[^1]);
    }

    [Fact]
    public async Task Constructor_ExposesNarrowChatContract()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        // Act
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, CreateTransportSupportPolicy(preferences), logger.Object, new TestCoreStringLocalizer());

        // Assert
        Assert.IsAssignableFrom<ISettingsChatConnection>(viewModel.Chat);
        Assert.False(viewModel.Chat is ChatViewModel);
        Assert.IsAssignableFrom<ISettingsAcpTransportConfiguration>(viewModel.Chat.TransportConfig);
        Assert.False(viewModel.Chat.TransportConfig is TransportConfigViewModel);
    }

    [Fact]
    public async Task GlobalAcpEnabled_ProjectsAppPreference()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(
            chat,
            profiles,
            preferences,
            CreateTransportSupportPolicy(preferences),
            logger.Object,
            new TestCoreStringLocalizer());

        Assert.True(viewModel.IsAcpEnabled);

        viewModel.IsAcpEnabled = false;

        Assert.False(preferences.AcpEnabled);
    }

    [Fact]
    public async Task TransportOptions_Should_PresentStdioAsSubprocessTransport()
    {
        var preferences = await CreatePreferencesAsync(supportsStdioTransport: true);
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, CreateTransportSupportPolicy(preferences), logger.Object, new TestCoreStringLocalizer());

        Assert.Equal("Stdio（子进程）", viewModel.TransportOptions[0].Name);
    }

    [Fact]
    public async Task Constructor_Should_CoerceStdioSelection_WhenSubprocessTransportUnsupported()
    {
        var preferences = await CreatePreferencesAsync(supportsStdioTransport: false);
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, CreateTransportSupportPolicy(preferences), logger.Object, new TestCoreStringLocalizer());

        Assert.DoesNotContain(viewModel.TransportOptions, option => option.Type == TransportType.Stdio);
        Assert.Equal(TransportType.WebSocket, viewModel.SelectedTransport?.Type);
        Assert.Equal(TransportType.WebSocket, chat.TransportConfig.SelectedTransportType);
    }

    [Fact]
    public async Task TransportConfigChange_Should_CoerceUnsupportedStdioSelection()
    {
        var preferences = await CreatePreferencesAsync(supportsStdioTransport: false);
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, CreateTransportSupportPolicy(preferences), logger.Object, new TestCoreStringLocalizer());

        chat.TransportConfig.SelectedTransportType = TransportType.Stdio;

        Assert.Equal(TransportType.WebSocket, chat.TransportConfig.SelectedTransportType);
        Assert.Equal(TransportType.WebSocket, viewModel.SelectedTransport?.Type);
    }

    [Fact]
    public async Task SelectedTransport_ChangeUpdatesTransportConfig()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, CreateTransportSupportPolicy(preferences), logger.Object, new TestCoreStringLocalizer());

        // Act
        viewModel.SelectedTransport = viewModel.TransportOptions[1];

        // Assert
        Assert.Equal(TransportType.WebSocket, chat.TransportConfig.SelectedTransportType);
        Assert.Equal("WebSocket", viewModel.SelectedTransportName);
    }

    [Fact]
    public async Task ConnectToProfileAsync_DelegatesToChatContract()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, CreateTransportSupportPolicy(preferences), logger.Object, new TestCoreStringLocalizer());
        var profile = new ServerConfiguration { Id = "profile-42", Name = "Selected Profile" };

        // Act
        await viewModel.ConnectToProfileAsync(profile);

        // Assert
        Assert.Same(profile, chat.ConnectedProfiles[^1]);
    }

    [Fact]
    public async Task HandleConnectionToggleAsync_ConnectsSelectedProfileWhenRequested()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var profile = new ServerConfiguration { Id = "profile-1", Name = "Profile 1" };
        profiles.Profiles.Add(profile);
        profiles.SelectedProfile = profile;
        var state = new TestConnectionState();
        var commands = new TestConnectionCommands();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(
            state,
            commands,
            new TestTransportConfiguration(),
            profiles,
            preferences,
            CreateTransportSupportPolicy(preferences),
            logger.Object,
            new TestCoreStringLocalizer());

        await viewModel.HandleConnectionToggleAsync(true);

        Assert.Same(profile, Assert.Single(commands.PoolConnectedProfiles));
        Assert.Empty(commands.ConnectedProfiles);
    }

    [Fact]
    public async Task HandleConnectionToggleAsync_DisconnectsSelectedProfileWhenRequested()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var profile = new ServerConfiguration { Id = "profile-1", Name = "Profile 1" };
        profiles.Profiles.Add(profile);
        profiles.SelectedProfile = profile;
        var state = new TestConnectionState();
        var commands = new TestConnectionCommands();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(
            state,
            commands,
            new TestTransportConfiguration(),
            profiles,
            preferences,
            CreateTransportSupportPolicy(preferences),
            logger.Object,
            new TestCoreStringLocalizer());

        await viewModel.HandleConnectionToggleAsync(false);

        Assert.Equal("profile-1", Assert.Single(commands.PoolDisconnectedProfileIds));
        Assert.Empty(commands.ConnectedProfiles);
        Assert.Equal(0, commands.DisconnectCallCount);
    }

    [Fact]
    public async Task HandleConnectionToggleAsync_ConnectRequestedWithoutSelectedProfile_DoesNothing()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var state = new TestConnectionState();
        var commands = new TestConnectionCommands();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(
            state,
            commands,
            new TestTransportConfiguration(),
            profiles,
            preferences,
            CreateTransportSupportPolicy(preferences),
            logger.Object,
            new TestCoreStringLocalizer());

        await viewModel.HandleConnectionToggleAsync(true);

        Assert.Empty(commands.ConnectedProfiles);
        Assert.Empty(commands.PoolConnectedProfiles);
        Assert.Equal(0, commands.DisconnectCallCount);
    }

    [Fact]
    public async Task RemoteDirectoryRows_SelectedProfile_ExposesOnlyProfileDirectories()
    {
        var preferences = CreatePreferences();
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory { ProfileId = "profile-a", DirectoryId = "dir-a-1", DisplayName = "Alpha One", RemotePath = "/remote/a-1" });
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory { ProfileId = "profile-b", DirectoryId = "dir-b-1", DisplayName = "Beta One", RemotePath = "/remote/b-1" });
        var viewModel = await CreateViewModelAsync(preferences);

        SelectProfile(viewModel, "profile-a");

        var row = Assert.Single(viewModel.RemoteDirectoryRows);
        Assert.Equal("Alpha One", row.DisplayName);
        Assert.Equal("/remote/a-1", row.RemotePath);
    }

    [Fact]
    public async Task RemoteDirectoryRows_AddUpdateRemove_UpdatesAppPreferencesDirectories()
    {
        var preferences = CreatePreferences();
        var viewModel = await CreateViewModelAsync(preferences);
        SelectProfile(viewModel, "profile-a");

        viewModel.AddRemoteDirectoryCommand.Execute(null);
        var row = Assert.Single(viewModel.RemoteDirectoryRows);
        row.DisplayName = " Workspace ";
        row.RemotePath = " /remote/workspace ";

        var directory = Assert.Single(preferences.AgentRemoteDirectories.Where(d => d.ProfileId == "profile-a"));
        Assert.False(string.IsNullOrWhiteSpace(directory.DirectoryId));
        Assert.Equal("Workspace", directory.DisplayName);
        Assert.Equal("/remote/workspace", directory.RemotePath);

        row.RemoveCommand.Execute(null);

        Assert.Empty(preferences.AgentRemoteDirectories.Where(d => d.ProfileId == "profile-a"));
    }

    [Fact]
    public async Task AddRemoteDirectoryCommand_NoSelectedProfile_DisablesAndSkipsMutation()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, CreateTransportSupportPolicy(preferences), logger.Object, new TestCoreStringLocalizer());
        var canExecute = viewModel.AddRemoteDirectoryCommand.CanExecute(null);
        viewModel.AddRemoteDirectoryCommand.Execute(null);

        Assert.False(canExecute);
        Assert.Empty(viewModel.RemoteDirectoryRows);
        Assert.Empty(preferences.AgentRemoteDirectories);
    }

    [Fact]
    public async Task RefreshCommand_WhenLegacyProfilesObserverFails_KeepsSettingsProfileItemsVisible()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var configurations = new[]
        {
            new ServerConfiguration
            {
                Id = "profile-b",
                Name = "Beta",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://localhost:9002"
            },
            new ServerConfiguration
            {
                Id = "profile-a",
                Name = "Alpha",
                Transport = TransportType.Stdio,
                StdioCommand = "alpha-acp"
            }
        };

        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.ListConfigurationsAsync())
            .ReturnsAsync(configurations);

        var registry = new InMemoryAcpConnectionSessionRegistry();
        var profiles = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            NullLogger<AcpProfilesViewModel>.Instance,
            registry,
            registry,
            new TestConnectionCommands(),
            NullLoggerFactory.Instance,
            new ImmediateUiDispatcher(),
            new TestCoreStringLocalizer());

        profiles.Profiles.CollectionChanged += ThrowWhenLegacyProfilesAdd;

        // Act
        await profiles.RefreshCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(new[] { "Alpha", "Beta" }, profiles.ProfileItems.Select(item => item.Name));
    }

    private static async Task<AppPreferencesViewModel> CreatePreferencesAsync(
        bool supportsStdioTransport = true,
        bool supportsLocalTerminal = true)
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());

        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);

        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(c => c.SupportsStdioTransport).Returns(supportsStdioTransport);
        capabilities.SetupGet(c => c.SupportsLocalTerminal).Returns(supportsLocalTerminal);
        capabilities.SetupGet(c => c.SupportsInteractiveTerminalSurface).Returns(true);
        var uiRuntime = new Mock<IUiRuntimeService>();
        var logger = new Mock<ILogger<AppPreferencesViewModel>>();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            logger.Object,
            new ImmediateUiDispatcher());

        await Task.Delay(10);
        return preferences;
    }

    /// <summary>
    /// Synchronous variant used by remote-directory row tests where the initial settings are
    /// already known and the preferences object is mutated before the ViewModel is created.
    /// </summary>
    private static AppPreferencesViewModel CreatePreferences()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());

        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);

        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(c => c.SupportsStdioTransport).Returns(true);
        capabilities.SetupGet(c => c.SupportsLocalTerminal).Returns(true);
        capabilities.SetupGet(c => c.SupportsInteractiveTerminalSurface).Returns(true);
        var uiRuntime = new Mock<IUiRuntimeService>();
        var logger = new Mock<ILogger<AppPreferencesViewModel>>();

        return new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            logger.Object,
            new ImmediateUiDispatcher());
    }

    private static async Task<AcpConnectionSettingsViewModel> CreateViewModelAsync(AppPreferencesViewModel preferences)
    {
        var profiles = CreateProfiles(preferences);
        profiles.Profiles.Add(new ServerConfiguration { Id = "profile-a", Name = "Profile A" });
        profiles.Profiles.Add(new ServerConfiguration { Id = "profile-b", Name = "Profile B" });

        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        var viewModel = new AcpConnectionSettingsViewModel(
            chat,
            profiles,
            preferences,
            CreateTransportSupportPolicy(preferences),
            logger.Object,
            new TestCoreStringLocalizer());

        await Task.Delay(10);
        return viewModel;
    }

    private static void SelectProfile(AcpConnectionSettingsViewModel viewModel, string profileId)
    {
        var profile = viewModel.Profiles.Profiles.FirstOrDefault(
            p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
        if (profile is not null)
        {
            viewModel.Profiles.SelectedProfile = profile;
        }
    }

    private static AcpProfilesViewModel CreateProfiles(AppPreferencesViewModel preferences)
    {
        var configurationService = new Mock<IConfigurationService>();
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();
        return new AcpProfilesViewModel(configurationService.Object, preferences, logger.Object, new ImmediateUiDispatcher());
    }

    private static ITransportSupportPolicy CreateTransportSupportPolicy(AppPreferencesViewModel preferences)
        => new TransportSupportPolicy(preferences.PlatformCapabilities);

    private static void ThrowWhenLegacyProfilesAdd(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add)
        {
            throw new InvalidOperationException("Simulates a downstream native selector projection failure.");
        }
    }

    private sealed class TestSettingsChatConnection : ISettingsChatConnection
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public ISettingsAcpTransportConfiguration TransportConfig { get; } = new TestTransportConfiguration();

        public string? AgentName { get; set; }

        public string? AgentVersion { get; set; }

        public bool IsConnecting { get; set; }

        public bool IsInitializing { get; set; }

        public bool IsConnected { get; set; }

        public string? ConnectionErrorMessage { get; set; }

        public bool HasConnectionError { get; set; }

        public IAsyncRelayCommand DisconnectCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

        public List<ServerConfiguration> ConnectedProfiles { get; } = new();

        public List<ServerConfiguration> PoolConnectedProfiles { get; } = new();
        public List<string> PoolDisconnectedProfileIds { get; } = new();

        public Task ConnectToAcpProfileAsync(ServerConfiguration profile)
        {
            ConnectedProfiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task ConnectProfileInPoolAsync(ServerConfiguration profile)
        {
            PoolConnectedProfiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task DisconnectProfileInPoolAsync(string profileId)
        {
            PoolDisconnectedProfileIds.Add(profileId);
            return Task.CompletedTask;
        }
    }

    private sealed class TestConnectionState : ISettingsAcpConnectionState
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string? AgentName { get; set; }

        public string? AgentVersion { get; set; }

        public bool IsConnecting { get; set; }

        public bool IsInitializing { get; set; }

        public bool IsConnected { get; set; }

        public string? ConnectionErrorMessage { get; set; }

        public bool HasConnectionError { get; set; }

        public void RaisePropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class TestConnectionCommands : ISettingsAcpConnectionCommands
    {
        public TestConnectionCommands()
        {
            DisconnectCommand = new AsyncRelayCommand(() =>
            {
                DisconnectCallCount++;
                return Task.CompletedTask;
            });
        }

        public IAsyncRelayCommand DisconnectCommand { get; }

        public int DisconnectCallCount { get; private set; }

        public List<ServerConfiguration> ConnectedProfiles { get; } = new();
        public List<ServerConfiguration> PoolConnectedProfiles { get; } = new();
        public List<string> PoolDisconnectedProfileIds { get; } = new();

        public Task ConnectToAcpProfileAsync(ServerConfiguration profile)
        {
            ConnectedProfiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task ConnectProfileInPoolAsync(ServerConfiguration profile)
        {
            PoolConnectedProfiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task DisconnectProfileInPoolAsync(string profileId)
        {
            PoolDisconnectedProfileIds.Add(profileId);
            return Task.CompletedTask;
        }
    }

    private sealed class TestTransportConfiguration : ISettingsAcpTransportConfiguration
    {
        private TransportType _selectedTransportType = TransportType.Stdio;
        private string _stdioCommand = string.Empty;
        private string _stdioArgs = string.Empty;
        private string _remoteUrl = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public TransportType SelectedTransportType
        {
            get => _selectedTransportType;
            set => SetField(ref _selectedTransportType, value, nameof(SelectedTransportType));
        }

        public string StdioCommand
        {
            get => _stdioCommand;
            set => SetField(ref _stdioCommand, value, nameof(StdioCommand));
        }

        public string StdioArgs
        {
            get => _stdioArgs;
            set => SetField(ref _stdioArgs, value, nameof(StdioArgs));
        }

        public string RemoteUrl
        {
            get => _remoteUrl;
            set => SetField(ref _remoteUrl, value, nameof(RemoteUrl));
        }

        public (bool IsValid, string? ErrorMessage) Validate() => (true, null);

        private void SetField<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
