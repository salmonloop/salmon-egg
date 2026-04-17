using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
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
            logger.Object);

        Assert.IsAssignableFrom<ISettingsChatConnection>(viewModel.Chat);
        Assert.Equal("Adapter Agent", viewModel.AgentDisplayName);

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
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, logger.Object);

        // Assert
        Assert.IsAssignableFrom<ISettingsChatConnection>(viewModel.Chat);
        Assert.False(viewModel.Chat is ChatViewModel);
        Assert.IsAssignableFrom<ISettingsAcpTransportConfiguration>(viewModel.Chat.TransportConfig);
        Assert.False(viewModel.Chat.TransportConfig is TransportConfigViewModel);
    }

    [Fact]
    public async Task ConnectionStatusText_ReflectsChatConnectionState()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, logger.Object);

        // Act
        chat.SetState(isConnecting: true);
        var connecting = viewModel.ConnectionStatusText;
        chat.SetState(isConnecting: false, isConnected: true);
        var connected = viewModel.ConnectionStatusText;
        chat.SetState(isConnected: false, connectionErrorMessage: "boom", hasConnectionError: true);
        var failed = viewModel.ConnectionStatusText;

        // Assert
        Assert.Equal("正在连接…", connecting);
        Assert.Equal("已连接", connected);
        Assert.Equal("连接失败", failed);
    }

    [Fact]
    public async Task CurrentEndpointDisplay_UpdatesWhenTransportConfigChanges()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, logger.Object);

        // Act
        chat.TransportConfig.StdioCommand = "agent";
        chat.TransportConfig.StdioArgs = "--mode run";
        var stdioEndpoint = viewModel.CurrentEndpointDisplay;

        chat.TransportConfig.SelectedTransportType = TransportType.WebSocket;
        chat.TransportConfig.RemoteUrl = "wss://example.test/socket";
        var remoteEndpoint = viewModel.CurrentEndpointDisplay;

        // Assert
        Assert.Equal("agent --mode run", stdioEndpoint);
        Assert.Equal("wss://example.test/socket", remoteEndpoint);
    }

    [Fact]
    public async Task TransportOptions_Should_PresentStdioAsSubprocessTransport()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, logger.Object);

        Assert.Equal("Stdio（子进程）", viewModel.TransportOptions[0].Name);
    }

    [Fact]
    public async Task AgentDisplayName_PrefersSelectedProfileNameAndFallsBackToAgentName()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Profile Alpha" });
        var chat = new TestSettingsChatConnection { AgentName = "Direct Agent" };
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, logger.Object);

        // Act
        preferences.LastSelectedServerId = "profile-1";
        var selectedProfileName = viewModel.AgentDisplayName;
        preferences.LastSelectedServerId = null;
        chat.SetAgentName("Fallback Agent");
        var fallbackAgentName = viewModel.AgentDisplayName;

        // Assert
        Assert.Equal("Profile Alpha", selectedProfileName);
        Assert.Equal("Fallback Agent", fallbackAgentName);
    }

    [Fact]
    public async Task SelectedTransport_ChangeUpdatesTransportConfig()
    {
        // Arrange
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, logger.Object);

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
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, logger.Object);
        var profile = new ServerConfiguration { Id = "profile-42", Name = "Selected Profile" };

        // Act
        await viewModel.ConnectToProfileAsync(profile);

        // Assert
        Assert.Same(profile, chat.ConnectedProfiles[^1]);
    }

    [Fact]
    public async Task HandleConnectionToggleAsync_ConnectsWhenRequestedAndCurrentlyDisconnected()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var state = new TestConnectionState { IsConnected = false };
        var commands = new TestConnectionCommands();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(
            state,
            commands,
            new TestTransportConfiguration(),
            profiles,
            preferences,
            logger.Object);

        await viewModel.HandleConnectionToggleAsync(true);

        Assert.Equal(1, commands.InitializeAndConnectCallCount);
        Assert.Equal(0, commands.DisconnectCallCount);
    }

    [Fact]
    public async Task HandleConnectionToggleAsync_DisconnectsWhenRequestedAndCurrentlyConnected()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var state = new TestConnectionState { IsConnected = true };
        var commands = new TestConnectionCommands();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(
            state,
            commands,
            new TestTransportConfiguration(),
            profiles,
            preferences,
            logger.Object);

        await viewModel.HandleConnectionToggleAsync(false);

        Assert.Equal(0, commands.InitializeAndConnectCallCount);
        Assert.Equal(1, commands.DisconnectCallCount);
    }

    [Fact]
    public async Task HandleConnectionToggleAsync_ReentrantState_DoesNothing()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var state = new TestConnectionState { IsConnecting = true };
        var commands = new TestConnectionCommands();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(
            state,
            commands,
            new TestTransportConfiguration(),
            profiles,
            preferences,
            logger.Object);

        await viewModel.HandleConnectionToggleAsync(true);

        Assert.Equal(0, commands.InitializeAndConnectCallCount);
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
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, logger.Object);
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
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, logger.Object);
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
        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, logger.Object);
        var canExecute = viewModel.AddPathMappingCommand.CanExecute(null);
        viewModel.AddPathMappingCommand.Execute(null);

        // Assert
        Assert.False(canExecute);
        Assert.Empty(viewModel.PathMappingRows);
        Assert.Empty(preferences.ProjectPathMappings);
    }

    [Fact]
    public async Task ProfileConnectionChanged_WhenRaisedOffUiThread_RefreshesDiagnosticsViaDispatcher()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var profile = new ServerConfiguration { Id = "profile-1", Name = "Profile 1" };
        profiles.Profiles.Add(profile);
        profiles.SelectedProfile = profile;

        var chat = new TestSettingsChatConnection();
        var registry = new TestSessionRegistry();
        var dispatcher = new QueueingUiDispatcher();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();
        var itemLogger = new Mock<ILogger<AgentProfileItemViewModel>>();

        profiles.SelectedProfileItem = new AgentProfileItemViewModel(
            profile,
            registry,
            registry,
            chat,
            itemLogger.Object,
            dispatcher);

        using var viewModel = new AcpConnectionSettingsViewModel(
            chat,
            profiles,
            registry,
            registry,
            preferences,
            logger.Object,
            dispatcher);

        Assert.Equal("Disconnected", viewModel.SelectedProfileStatus);

        registry.ConnectedSession = CreateSessionSnapshot("profile-1", "OpenCode", "1.3.15");
        registry.IsConnected = true;
        registry.RaiseProfileConnectionChanged("profile-1", true);

        Assert.Equal("Disconnected", viewModel.SelectedProfileStatus);

        dispatcher.RunAll();

        Assert.Equal("Connected", viewModel.SelectedProfileStatus);
        Assert.Equal("OpenCode", viewModel.SelectedProfileAgentName);
        Assert.Equal("1.3.15", viewModel.SelectedProfileAgentVersion);
    }

    private static async Task<AppPreferencesViewModel> CreatePreferencesAsync()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());

        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);

        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
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

    private sealed class TestSettingsChatConnection : ISettingsChatConnection
    {
        private string? _agentName;
        private bool _isConnecting;
        private bool _isInitializing;
        private bool _isConnected;
        private string? _connectionErrorMessage;
        private bool _hasConnectionError;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ISettingsAcpTransportConfiguration TransportConfig { get; } = new TestTransportConfiguration();

        public string? AgentName
        {
            get => _agentName;
            set => SetField(ref _agentName, value, nameof(AgentName));
        }

        public string? AgentVersion { get; set; }

        public bool IsConnecting
        {
            get => _isConnecting;
            private set => SetField(ref _isConnecting, value, nameof(IsConnecting));
        }

        public bool IsInitializing
        {
            get => _isInitializing;
            private set => SetField(ref _isInitializing, value, nameof(IsInitializing));
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set => SetField(ref _isConnected, value, nameof(IsConnected));
        }

        public string? ConnectionErrorMessage
        {
            get => _connectionErrorMessage;
            private set => SetField(ref _connectionErrorMessage, value, nameof(ConnectionErrorMessage));
        }

        public bool HasConnectionError
        {
            get => _hasConnectionError;
            private set => SetField(ref _hasConnectionError, value, nameof(HasConnectionError));
        }

        public IAsyncRelayCommand InitializeAndConnectCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

        public IAsyncRelayCommand DisconnectCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

        public List<ServerConfiguration> ConnectedProfiles { get; } = new();

        public Task ConnectToAcpProfileAsync(ServerConfiguration profile)
        {
            ConnectedProfiles.Add(profile);
            return Task.CompletedTask;
        }

        public void SetState(
            bool isConnecting = false,
            bool isInitializing = false,
            bool isConnected = false,
            string? connectionErrorMessage = null,
            bool hasConnectionError = false)
        {
            IsConnecting = isConnecting;
            IsInitializing = isInitializing;
            IsConnected = isConnected;
            ConnectionErrorMessage = connectionErrorMessage;
            HasConnectionError = hasConnectionError;
        }

        public void SetAgentName(string? agentName)
        {
            AgentName = agentName;
        }

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
            InitializeAndConnectCommand = new AsyncRelayCommand(() =>
            {
                InitializeAndConnectCallCount++;
                return Task.CompletedTask;
            });

            DisconnectCommand = new AsyncRelayCommand(() =>
            {
                DisconnectCallCount++;
                return Task.CompletedTask;
            });
        }

        public IAsyncRelayCommand InitializeAndConnectCommand { get; }

        public IAsyncRelayCommand DisconnectCommand { get; }

        public int InitializeAndConnectCallCount { get; private set; }

        public int DisconnectCallCount { get; private set; }

        public List<ServerConfiguration> ConnectedProfiles { get; } = new();

        public Task ConnectToAcpProfileAsync(ServerConfiguration profile)
        {
            ConnectedProfiles.Add(profile);
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

    private sealed class TestSessionRegistry : IAcpConnectionSessionRegistry, IAcpConnectionSessionEvents
    {
        public event Action<string, bool>? ProfileConnectionChanged;

        public bool IsConnected { get; set; }
        public AcpConnectionSession? ConnectedSession { get; set; }

        public bool TryGetByProfile(string profileId, out AcpConnectionSession session)
        {
            if (IsConnected && ConnectedSession != null)
            {
                session = ConnectedSession;
                return true;
            }

            session = null!;
            return false;
        }

        public bool TryGetProfileId(IChatService service, out string profileId)
        {
            profileId = string.Empty;
            return false;
        }

        public void Upsert(AcpConnectionSession session) => throw new NotSupportedException();

        public bool RemoveByProfile(string profileId) => throw new NotSupportedException();

        public bool RemoveByService(IChatService service, out string profileId) => throw new NotSupportedException();

        public IReadOnlyList<AcpConnectionSession> RemoveWhere(Func<AcpConnectionSession, bool> predicate) => throw new NotSupportedException();

        public bool Touch(string profileId, DateTime? usedAtUtc = null) => throw new NotSupportedException();

        public IReadOnlyList<AcpConnectionSession> GetSnapshot() => Array.Empty<AcpConnectionSession>();

        public void RaiseProfileConnectionChanged(string profileId, bool isConnected)
            => ProfileConnectionChanged?.Invoke(profileId, isConnected);
    }

    private static AcpConnectionSession CreateSessionSnapshot(string profileId, string agentName, string agentVersion)
    {
        var service = new Mock<IChatService>();
        service.SetupGet(x => x.IsConnected).Returns(true);
        service.SetupGet(x => x.IsInitialized).Returns(true);
        service.SetupGet(x => x.AgentCapabilities).Returns(new AgentCapabilities());

        var adapter = new AcpChatServiceAdapter(
            service.Object,
            new AcpEventAdapter(
                _ => { },
                new ImmediateUiDispatcher(),
                bufferLimit: 16,
                resyncRequired: _ => { }));

        return new AcpConnectionSession(
            profileId,
            adapter,
            new InitializeResponse(1, new AgentInfo(agentName, agentVersion), new AgentCapabilities()),
            new AcpConnectionReuseKey(TransportType.Stdio, "ssh", "oci-arm", string.Empty));
    }
}
