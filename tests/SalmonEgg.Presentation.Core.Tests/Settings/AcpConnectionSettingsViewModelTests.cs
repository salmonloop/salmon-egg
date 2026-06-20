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
    public async Task RemoteDirectoryRows_ExposeSharedDirectoriesRegardlessOfSelectedProfile()
    {
        var preferences = CreatePreferences();
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory { DirectoryId = "dir-a-1", DisplayName = "Alpha One", RemotePath = "/remote/a-1" });
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory { DirectoryId = "dir-b-1", DisplayName = "Beta One", RemotePath = "/remote/b-1" });
        var viewModel = await CreateViewModelAsync(preferences);

        SelectProfile(viewModel, "profile-a");

        Assert.Collection(
            viewModel.RemoteDirectoryRows,
            first =>
            {
                Assert.Equal("Alpha One", first.DisplayName);
                Assert.Equal("/remote/a-1", first.RemotePath);
                Assert.False(first.IsEditing);
                Assert.False(first.IsNew);
            },
            second =>
            {
                Assert.Equal("Beta One", second.DisplayName);
                Assert.Equal("/remote/b-1", second.RemotePath);
                Assert.False(second.IsEditing);
                Assert.False(second.IsNew);
            });
    }

    [Fact]
    public async Task RemoteDirectoryRows_AddCreatesExpandedDraftRow()
    {
        var preferences = CreatePreferences();
        var viewModel = await CreateViewModelAsync(preferences);
        SelectProfile(viewModel, "profile-a");

        viewModel.AddRemoteDirectoryCommand.Execute(null);

        var row = Assert.Single(viewModel.RemoteDirectoryRows);
        Assert.True(row.IsEditing);
        Assert.True(row.IsNew);
        Assert.Equal(string.Empty, row.DisplayName);
        Assert.Equal(string.Empty, row.RemotePath);
        Assert.Equal(string.Empty, row.DisplayNameDraft);
        Assert.Equal(string.Empty, row.RemotePathDraft);
    }

    [Fact]
    public async Task RemoteDirectoryRows_SaveCommitsDraftAndCollapsesRow()
    {
        var preferences = CreatePreferences();
        var viewModel = await CreateViewModelAsync(preferences);
        SelectProfile(viewModel, "profile-a");

        viewModel.AddRemoteDirectoryCommand.Execute(null);
        var row = Assert.Single(viewModel.RemoteDirectoryRows);
        row.DisplayNameDraft = " Workspace ";
        row.RemotePathDraft = " /remote/workspace ";

        await row.SaveCommand.ExecuteAsync(null);

        var directory = Assert.Single(preferences.AgentRemoteDirectories);
        Assert.False(string.IsNullOrWhiteSpace(directory.DirectoryId));
        Assert.Equal("Workspace", directory.DisplayName);
        Assert.Equal("/remote/workspace", directory.RemotePath);
        Assert.False(row.IsEditing);
        Assert.False(row.IsNew);
        Assert.Equal("Workspace", row.DisplayName);
        Assert.Equal("/remote/workspace", row.RemotePath);
        Assert.Equal("Workspace", row.DisplayNameDraft);
        Assert.Equal("/remote/workspace", row.RemotePathDraft);
    }

    [Fact]
    public async Task RemoteDirectoryRows_SaveWithEmptyRemotePath_ShowsValidationAndKeepsEditing()
    {
        var preferences = CreatePreferences();
        var viewModel = await CreateViewModelAsync(preferences);
        SelectProfile(viewModel, "profile-a");

        viewModel.AddRemoteDirectoryCommand.Execute(null);
        var row = Assert.Single(viewModel.RemoteDirectoryRows);
        row.DisplayNameDraft = " Workspace ";
        row.RemotePathDraft = " ";

        await row.SaveCommand.ExecuteAsync(null);

        Assert.True(row.IsEditing);
        Assert.True(row.IsNew);
        Assert.False(string.IsNullOrWhiteSpace(row.ValidationMessage));
        Assert.Empty(preferences.AgentRemoteDirectories);
    }

    [Fact]
    public async Task RemoteDirectoryRows_CancelOnExistingRow_RestoresPersistedValues()
    {
        var preferences = CreatePreferences();
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = "dir-a-1",
            DisplayName = "Alpha One",
            RemotePath = "/remote/a-1"
        });
        var viewModel = await CreateViewModelAsync(preferences);
        SelectProfile(viewModel, "profile-a");

        var row = Assert.Single(viewModel.RemoteDirectoryRows);
        row.BeginEditCommand.Execute(null);
        row.DisplayNameDraft = "Changed";
        row.RemotePathDraft = "/remote/changed";

        row.CancelCommand.Execute(null);

        var directory = Assert.Single(preferences.AgentRemoteDirectories);
        Assert.Equal("Alpha One", directory.DisplayName);
        Assert.Equal("/remote/a-1", directory.RemotePath);
        Assert.False(row.IsEditing);
        Assert.Equal("Alpha One", row.DisplayName);
        Assert.Equal("/remote/a-1", row.RemotePath);
        Assert.Equal("Alpha One", row.DisplayNameDraft);
        Assert.Equal("/remote/a-1", row.RemotePathDraft);
    }

    [Fact]
    public async Task RemoteDirectoryRows_BeginEdit_AllowsOnlyOneExpandedRow()
    {
        var preferences = CreatePreferences();
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = "dir-a-1",
            DisplayName = "Alpha One",
            RemotePath = "/remote/a-1"
        });
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = "dir-a-2",
            DisplayName = "Alpha Two",
            RemotePath = "/remote/a-2"
        });
        var viewModel = await CreateViewModelAsync(preferences);
        SelectProfile(viewModel, "profile-a");

        var first = viewModel.RemoteDirectoryRows[0];
        var second = viewModel.RemoteDirectoryRows[1];

        first.BeginEditCommand.Execute(null);
        second.BeginEditCommand.Execute(null);

        Assert.True(first.IsEditing);
        Assert.False(second.IsEditing);
    }

    [Fact]
    public async Task RemoteDirectoryRows_ProfileSelectionChangeWhileEditing_PreservesDraftWithoutBlockingProfileSelection()
    {
        var preferences = CreatePreferences();
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = "dir-a-1",
            DisplayName = "Alpha One",
            RemotePath = "/remote/a-1"
        });
        using var profiles = await CreateProfilesWithItemsAsync(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(
            chat,
            profiles,
            preferences,
            CreateTransportSupportPolicy(preferences),
            logger.Object,
            new TestCoreStringLocalizer());

        SelectProfileItem(viewModel, "profile-a");

        var row = Assert.Single(viewModel.RemoteDirectoryRows);
        row.BeginEditCommand.Execute(null);
        row.DisplayNameDraft = "Workspace";
        row.RemotePathDraft = "/remote/workspace";

        SelectProfileItem(viewModel, "profile-b");

        Assert.Equal("profile-b", viewModel.Profiles.SelectedProfile?.Id);
        Assert.Equal("profile-b", viewModel.Profiles.SelectedProfileItem?.ProfileId);
        Assert.Single(viewModel.RemoteDirectoryRows);
        Assert.True(row.IsEditing);
        Assert.Equal("Workspace", row.DisplayNameDraft);
        Assert.Equal("/remote/workspace", row.RemotePathDraft);
    }

    [Fact]
    public async Task RemoteDirectoryRows_CancelOnNewRow_RemovesRowAndDoesNotPersist()
    {
        var preferences = CreatePreferences();
        var viewModel = await CreateViewModelAsync(preferences);
        SelectProfile(viewModel, "profile-a");

        viewModel.AddRemoteDirectoryCommand.Execute(null);
        var row = Assert.Single(viewModel.RemoteDirectoryRows);
        row.DisplayNameDraft = " Workspace ";
        row.RemotePathDraft = " /remote/workspace ";

        row.CancelCommand.Execute(null);

        Assert.Empty(viewModel.RemoteDirectoryRows);
        Assert.Empty(preferences.AgentRemoteDirectories);
    }

    [Fact]
    public async Task RemoteDirectoryRows_RemoveRemovesPersistedDirectory()
    {
        var preferences = CreatePreferences();
        var viewModel = await CreateViewModelAsync(preferences);
        SelectProfile(viewModel, "profile-a");

        viewModel.AddRemoteDirectoryCommand.Execute(null);
        var row = Assert.Single(viewModel.RemoteDirectoryRows);
        row.DisplayNameDraft = " Workspace ";
        row.RemotePathDraft = " /remote/workspace ";
        await row.SaveCommand.ExecuteAsync(null);

        row.RemoveCommand.Execute(null);

        Assert.Empty(preferences.AgentRemoteDirectories);
    }

    [Fact]
    public async Task RemoteDirectoryRows_SaveWithNonAbsoluteRemotePath_ShowsValidationAndKeepsEditing()
    {
        var preferences = CreatePreferences();
        var viewModel = await CreateViewModelAsync(preferences);
        SelectProfile(viewModel, "profile-a");

        viewModel.AddRemoteDirectoryCommand.Execute(null);
        var row = Assert.Single(viewModel.RemoteDirectoryRows);
        row.DisplayNameDraft = "Workspace";
        row.RemotePathDraft = "relative/path";

        await row.SaveCommand.ExecuteAsync(null);

        Assert.True(row.IsEditing);
        Assert.True(row.IsNew);
        Assert.False(string.IsNullOrWhiteSpace(row.ValidationMessage));
        Assert.Empty(preferences.AgentRemoteDirectories);
    }

    [Fact]
    public async Task RemoteDirectoryRows_SaveUpdatesExistingSharedDirectory()
    {
        var preferences = CreatePreferences();
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = "dir-a-1",
            DisplayName = string.Empty,
            RemotePath = "/remote/a-1"
        });
        var viewModel = await CreateViewModelAsync(preferences);
        SelectProfile(viewModel, "profile-b");

        var row = new AcpRemoteDirectoryRowViewModel(
            new AgentRemoteDirectory
            {
                DirectoryId = "dir-a-1",
                DisplayName = "Alpha One",
                RemotePath = "/remote/a-1"
            },
            viewModel);
        row.BeginEditCommand.Execute(null);
        row.DisplayNameDraft = "Workspace";
        row.RemotePathDraft = "/remote/workspace";

        await viewModel.SaveRemoteDirectoryAsync(row);

        var directory = Assert.Single(preferences.AgentRemoteDirectories);
        Assert.Equal("Workspace", directory.DisplayName);
        Assert.Equal("/remote/workspace", directory.RemotePath);
    }

    [Fact]
    public async Task RemoteDirectoryRows_SaveWithDuplicateRemotePath_KeepsNewestSharedDirectory()
    {
        var preferences = CreatePreferences();
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            DirectoryId = "dir-a",
            DisplayName = "Alpha",
            RemotePath = "/remote/shared"
        });
        var viewModel = await CreateViewModelAsync(preferences);

        viewModel.AddRemoteDirectoryCommand.Execute(null);
        var draftRow = Assert.Single(viewModel.RemoteDirectoryRows.Where(row => row.IsNew));
        draftRow.DisplayNameDraft = "Shared Workspace";
        draftRow.RemotePathDraft = "/remote/shared";

        await draftRow.SaveCommand.ExecuteAsync(null);

        var directory = Assert.Single(preferences.AgentRemoteDirectories);
        Assert.Equal("Shared Workspace", directory.DisplayName);
        Assert.Equal("/remote/shared", directory.RemotePath);
    }

    [Fact]
    public async Task AddRemoteDirectoryCommand_NoSelectedProfile_CreatesSharedDraftRow()
    {
        var preferences = await CreatePreferencesAsync();
        var profiles = CreateProfiles(preferences);
        var chat = new TestSettingsChatConnection();
        var logger = new Mock<ILogger<AcpConnectionSettingsViewModel>>();

        using var viewModel = new AcpConnectionSettingsViewModel(chat, profiles, preferences, CreateTransportSupportPolicy(preferences), logger.Object, new TestCoreStringLocalizer());
        var canExecute = viewModel.AddRemoteDirectoryCommand.CanExecute(null);
        viewModel.AddRemoteDirectoryCommand.Execute(null);

        Assert.True(canExecute);
        var row = Assert.Single(viewModel.RemoteDirectoryRows);
        Assert.True(row.IsEditing);
        Assert.True(row.IsNew);
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

    [Fact]
    public async Task RefreshCommand_WhenExistingProfileIsRenamed_UpdatesSettingsProfileItem()
    {
        var preferences = await CreatePreferencesAsync();
        var configurations = new[]
        {
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
            .ReturnsAsync(() => configurations);

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

        await profiles.RefreshCommand.ExecuteAsync(null);
        var item = Assert.Single(profiles.ProfileItems);
        Assert.Equal("Alpha", item.Name);

        configurations[0] = new ServerConfiguration
        {
            Id = "profile-a",
            Name = "Alpha Renamed",
            Transport = TransportType.WebSocket,
            ServerUrl = "ws://localhost:9001"
        };

        await profiles.RefreshCommand.ExecuteAsync(null);

        var updated = Assert.Single(profiles.ProfileItems);
        Assert.Same(item, updated);
        Assert.Equal("Alpha Renamed", updated.Name);
        Assert.Equal("ws://localhost:9001", updated.EndpointDescription);
    }

    [Fact]
    public async Task RefreshCommand_WhenProfileRenameChangesSortOrder_ReordersSettingsProfileItems()
    {
        var preferences = await CreatePreferencesAsync();
        var configurations = new[]
        {
            new ServerConfiguration
            {
                Id = "profile-a",
                Name = "Alpha",
                Transport = TransportType.Stdio,
                StdioCommand = "alpha-acp"
            },
            new ServerConfiguration
            {
                Id = "profile-b",
                Name = "Beta",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://localhost:9002"
            }
        };

        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.ListConfigurationsAsync())
            .ReturnsAsync(() => configurations);

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

        await profiles.RefreshCommand.ExecuteAsync(null);
        profiles.SelectedProfileItem = profiles.ProfileItems.Single(item => item.ProfileId == "profile-b");

        configurations = new[]
        {
            new ServerConfiguration
            {
                Id = "profile-a",
                Name = "Zeta",
                Transport = TransportType.Stdio,
                StdioCommand = "alpha-acp"
            },
            new ServerConfiguration
            {
                Id = "profile-b",
                Name = "Aardvark",
                Transport = TransportType.WebSocket,
                ServerUrl = "ws://localhost:9002"
            }
        };

        await profiles.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "profile-b", "profile-a" }, profiles.ProfileItems.Select(item => item.ProfileId));
        Assert.Equal("profile-b", profiles.SelectedProfileItem?.ProfileId);
        Assert.Equal("profile-b", profiles.SelectedProfile?.Id);
    }

    [Fact]
    public async Task SelectedProfile_WhenSetDirectly_UpdatesSettingsProfileItemSelection()
    {
        var preferences = await CreatePreferencesAsync();
        using var profiles = await CreateProfilesWithItemsAsync(preferences);

        profiles.SelectedProfile = profiles.Profiles.Single(profile => profile.Id == "profile-b");

        Assert.Equal("profile-b", profiles.SelectedProfile?.Id);
        Assert.Equal("profile-b", profiles.SelectedProfileItem?.ProfileId);
    }

    [Fact]
    public async Task RefreshCommand_WhenSelectedProfileObjectIsReplaced_PreservesSelectionByProfileId()
    {
        var preferences = await CreatePreferencesAsync();
        var configurations = new[]
        {
            new ServerConfiguration { Id = "profile-a", Name = "Alpha" },
            new ServerConfiguration { Id = "profile-b", Name = "Beta" }
        };

        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.ListConfigurationsAsync())
            .ReturnsAsync(() => configurations);

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

        await profiles.RefreshCommand.ExecuteAsync(null);
        profiles.SelectedProfile = profiles.Profiles.Single(profile => profile.Id == "profile-b");

        configurations = new[]
        {
            new ServerConfiguration { Id = "profile-a", Name = "Alpha" },
            new ServerConfiguration { Id = "profile-b", Name = "Beta Renamed" }
        };

        await profiles.RefreshCommand.ExecuteAsync(null);

        Assert.Same(profiles.Profiles.Single(profile => profile.Id == "profile-b"), profiles.SelectedProfile);
        Assert.Equal("profile-b", profiles.SelectedProfileItem?.ProfileId);
        Assert.Equal("Beta Renamed", profiles.SelectedProfileItem?.Name);
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

    private static async Task<AcpProfilesViewModel> CreateProfilesWithItemsAsync(AppPreferencesViewModel preferences)
    {
        var configurations = new[]
        {
            new ServerConfiguration { Id = "profile-a", Name = "Profile A" },
            new ServerConfiguration { Id = "profile-b", Name = "Profile B" }
        };

        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.ListConfigurationsAsync())
            .ReturnsAsync(configurations);

        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            logger.Object,
            registry,
            registry,
            new TestConnectionCommands(),
            NullLoggerFactory.Instance,
            new ImmediateUiDispatcher(),
            new TestCoreStringLocalizer());

        await profiles.RefreshAsync();
        return profiles;
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

    private static void SelectProfileItem(AcpConnectionSettingsViewModel viewModel, string profileId)
    {
        var item = viewModel.Profiles.ProfileItems.FirstOrDefault(
            vm => string.Equals(vm.ProfileId, profileId, StringComparison.Ordinal));
        if (item is not null)
        {
            viewModel.Profiles.SelectedProfileItem = item;
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
