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
using SalmonEgg.Domain.Models.ProjectAffinity;
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
    public async Task PathMappingRows_SelectedProfile_ExposesOnlyProfileMappings()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        preferences.ProjectPathMappings.Add(new ProjectPathMapping
        {
            ProfileId = "profile-a",
            RemoteRootPath = "/remote/a-1",
            LocalRootPath = "C:\\work\\a-1"
        });
        preferences.ProjectPathMappings.Add(new ProjectPathMapping
        {
            ProfileId = "profile-a",
            RemoteRootPath = "/remote/a-2",
            LocalRootPath = "C:\\work\\a-2"
        });
        preferences.ProjectPathMappings.Add(new ProjectPathMapping
        {
            ProfileId = "profile-b",
            RemoteRootPath = "/remote/b-1",
            LocalRootPath = "C:\\work\\b-1"
        });

        var profiles = CreateProfiles(preferences);
        profiles.Profiles.Add(new ServerConfiguration { Id = "profile-a", Name = "Profile A" });
        profiles.Profiles.Add(new ServerConfiguration { Id = "profile-b", Name = "Profile B" });
        profiles.SelectedProfile = profiles.Profiles[0];

        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        // Act
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, CreateTransportSupportPolicy(preferences), logger.Object, new TestCoreStringLocalizer());
        var profileARows = viewModel.PathMappingRows.ToArray();
        profiles.SelectedProfile = profiles.Profiles[1];
        var profileBRows = viewModel.PathMappingRows.ToArray();

        // Assert
        Assert.Equal(2, profileARows.Length);
        Assert.All(profileARows, row => Assert.Equal("profile-a", row.ProfileId));
        Assert.Single(profileBRows);
        Assert.Equal("profile-b", profileBRows[0].ProfileId);
    }

    [Fact]
    public async Task PathMappingRows_AddUpdateRemove_UpdatesAppPreferencesMappings()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        profiles.Profiles.Add(new ServerConfiguration { Id = "profile-a", Name = "Profile A" });
        profiles.SelectedProfile = profiles.Profiles[0];

        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        // Act
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, CreateTransportSupportPolicy(preferences), logger.Object, new TestCoreStringLocalizer());
        viewModel.AddPathMappingCommand.Execute(null);
        var row = Assert.Single(viewModel.PathMappingRows);
        row.RemoteRootPath = " /remote/workspace ";
        row.LocalRootPath = " C:\\work\\workspace ";

        // Assert
        var mapping = Assert.Single(preferences.ProjectPathMappings.Where(m => m.ProfileId == "profile-a"));
        Assert.Equal("/remote/workspace", mapping.RemoteRootPath);
        Assert.Equal("C:\\work\\workspace", mapping.LocalRootPath);

        // Act
        row.RemoveCommand.Execute(null);

        // Assert
        Assert.Empty(preferences.ProjectPathMappings.Where(m => m.ProfileId == "profile-a"));
    }

    [Fact]
    public async Task AddPathMappingCommand_NoSelectedProfile_DisablesAndSkipsMutation()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        // Act
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, CreateTransportSupportPolicy(preferences), logger.Object, new TestCoreStringLocalizer());
        var canExecute = viewModel.AddPathMappingCommand.CanExecute(null);
        viewModel.AddPathMappingCommand.Execute(null);

        // Assert
        Assert.False(canExecute);
        Assert.Empty(viewModel.PathMappingRows);
        Assert.Empty(preferences.ProjectPathMappings);
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
