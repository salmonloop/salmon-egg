using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;
using SerilogLogger = Serilog.ILogger;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public class ChatViewModelTests
{
    private static ViewModelFixture CreateViewModel(
        SynchronizationContext? syncContext = null,
        Mock<IConversationStore>? conversationStore = null,
        IAcpConnectionCommands? acpConnectionCommands = null,
        Mock<IConfigurationService>? configurationService = null,
        Mock<ISessionManager>? sessionManager = null,
        IConversationActivationCoordinator? conversationActivationCoordinator = null,
        Func<IChatConnectionStore, IAcpConnectionCoordinator>? acpConnectionCoordinatorFactory = null)
    {
        var state = State.Value(new object(), () => ChatState.Empty);
        var connectionState = State.Value(new object(), () => ChatConnectionState.Empty);
        var connectionStore = new ChatConnectionStore(connectionState);
        var chatStore = new RecordingChatStore(state);
        var transportFactory = new Mock<ITransportFactory>();
        var messageParser = new Mock<IMessageParser>();
        var messageValidator = new Mock<IMessageValidator>();
        var errorLogger = new Mock<IErrorLogger>();
        var ownsSessionManager = sessionManager is null;
        sessionManager ??= new Mock<ISessionManager>();
        if (ownsSessionManager)
        {
            var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
            sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
                .Returns<string, string?>((sessionId, cwd) =>
                {
                    var session = new Session(sessionId, cwd);
                    sessions[sessionId] = session;
                    return Task.FromResult(session);
                });
            sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
                .Returns<string>(sessionId => sessions.TryGetValue(sessionId, out var session) ? session : null);
            sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
                .Returns<string, Action<Session>, bool>((sessionId, update, updateActivity) =>
                {
                    if (!sessions.TryGetValue(sessionId, out var session))
                    {
                        return false;
                    }

                    update(session);
                    if (updateActivity)
                    {
                        session.UpdateActivity();
                    }

                    return true;
                });
            sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
                .Returns<string>(sessionId => sessions.Remove(sessionId));
        }
        var serilog = new Mock<SerilogLogger>();

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            sessionManager.Object,
            serilog.Object);

        configurationService ??= new Mock<IConfigurationService>();
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object);

        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configurationService.Object, preferences, profilesLogger.Object);

        var ownsConversationStore = conversationStore is null;
        conversationStore ??= new Mock<IConversationStore>();
        if (ownsConversationStore)
        {
            conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConversationDocument());
        }
        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var workspace = new ChatConversationWorkspace(
            sessionManager.Object,
            conversationStore.Object,
            new AppPreferencesConversationWorkspacePreferences(preferences),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            syncContext ?? SynchronizationContext.Current ?? new SynchronizationContext());
        var conversationCatalogPresenter = new ConversationCatalogPresenter();
        var vmLogger = new Mock<ILogger<ChatViewModel>>();

        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(syncContext ?? new SynchronizationContext());
            var chatStateProjector = new ChatStateProjector();

            var viewModel = new ChatViewModel(
                chatStore,
                chatServiceFactory,
                configurationService.Object,
                preferences,
                profiles,
                sessionManager.Object,
                miniWindow.Object,
                workspace,
                conversationCatalogPresenter,
                chatStateProjector,
                null,
                connectionStore,
                vmLogger.Object,
                syncContext,
                acpConnectionCommands,
                conversationActivationCoordinator: conversationActivationCoordinator,
                acpConnectionCoordinator: acpConnectionCoordinatorFactory?.Invoke(connectionStore));
            return new ViewModelFixture(
                viewModel,
                state,
                connectionState,
                connectionStore,
                chatStore,
                workspace,
                conversationStore,
                preferences,
                profiles,
                chatStore,
                vmLogger);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task RestoreAsync_IsExplicitAndOnlyRestoresWorkspaceOnce()
    {
        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument());

        await using var fixture = CreateViewModel(conversationStore: conversationStore);

        conversationStore.Verify(s => s.LoadAsync(It.IsAny<CancellationToken>()), Times.Never);

        await fixture.ViewModel.RestoreAsync();
        await fixture.ViewModel.RestoreAsync();

        conversationStore.Verify(s => s.LoadAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplaceChatService_WhenCalledOffUiContext_DoesNotBlockCaller()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();

        var replaceTask = Task.Run(() => fixture.ViewModel.ReplaceChatService(chatService.Object));
        var completedTask = await Task.WhenAny(replaceTask, Task.Delay(250));

        Assert.Same(replaceTask, completedTask);

        syncContext.RunAll();
        await replaceTask;

        Assert.Same(chatService.Object, fixture.ViewModel.CurrentChatService);
    }

    [Fact]
    public async Task SelectProfile_WhenCalledOffUiContext_DoesNotBlockCaller()
    {
        var syncContext = new QueueingSynchronizationContext();
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Remote Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe"
        };

        await using var fixture = CreateViewModel(syncContext);
        fixture.Profiles.Profiles.Add(profile);

        var selectTask = Task.Run(() => fixture.ViewModel.SelectProfile(profile));
        var completedTask = await Task.WhenAny(selectTask, Task.Delay(250));

        Assert.Same(selectTask, completedTask);

        syncContext.RunAll();
        await selectTask;

        Assert.Same(profile, fixture.ViewModel.SelectedAcpProfile);

        await WaitForConditionAsync(async () =>
        {
            var connectionState = await fixture.GetConnectionStateAsync();
            return string.Equals(connectionState.SelectedProfileId, "profile-1", StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RestoreAsync_BootstrapsPersistedBindingsIntoStore()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument
            {
                LastActiveConversationId = "session-1",
                Conversations =
                {
                    new ConversationRecord
                    {
                        ConversationId = "session-1",
                        DisplayName = "Session One",
                        CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedAt = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                        RemoteSessionId = "remote-1",
                        BoundProfileId = "profile-a",
                        Messages =
                        {
                            new ConversationMessageSnapshot
                            {
                                Id = "m-1",
                                Timestamp = new DateTime(2026, 3, 2, 1, 0, 0, DateTimeKind.Utc),
                                ContentType = "text",
                                TextContent = "hello from restore"
                            }
                        }
                    }
                }
            });

        await using var fixture = CreateViewModel(
            syncContext,
            conversationStore: conversationStore,
            sessionManager: sessionManager);

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());
        syncContext.RunAll();
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "session-1", StringComparison.Ordinal)
                && fixture.ViewModel.MessageHistory.Count == 1
                && fixture.ViewModel.AvailableModes.Count == 0
                && fixture.ViewModel.SelectedMode is null
                && fixture.ViewModel.ConfigOptions.Count == 0);
        });

        conversationStore.Verify(s => s.LoadAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(new[] { "session-1" }, fixture.Workspace.GetKnownConversationIds());

        var workspaceBinding = fixture.Workspace.GetRemoteBinding("session-1");
        Assert.Equal("remote-1", workspaceBinding?.RemoteSessionId);
        Assert.Equal("profile-a", workspaceBinding?.BoundProfileId);

        var dispatchedActions = fixture.ChatStore.Actions.ToArray();
        Assert.Contains(dispatchedActions, action =>
            action is SetBindingSliceAction
            {
                Binding: { ConversationId: "session-1", RemoteSessionId: "remote-1", ProfileId: "profile-a" }
            });

        var state = await fixture.GetStateAsync();
        Assert.Equal(new ConversationBindingSlice("session-1", "remote-1", "profile-a"), state.ResolveBinding("session-1"));
        Assert.NotNull(state.Transcript);
        Assert.Single(state.Transcript!);
        Assert.Equal("hello from restore", state.Transcript[0].TextContent);
        Assert.NotNull(state.AvailableModes);
        Assert.Empty(state.AvailableModes!);
        Assert.Null(state.SelectedModeId);
        Assert.NotNull(state.ConfigOptions);
        Assert.Empty(state.ConfigOptions!);
        Assert.False(state.ShowConfigOptionsPanel);
        Assert.Equal("session-1", fixture.ViewModel.CurrentSessionId);
        Assert.Equal("remote-1", fixture.ViewModel.CurrentRemoteSessionId);
        Assert.Single(fixture.ViewModel.MessageHistory);
        Assert.Equal("hello from restore", fixture.ViewModel.MessageHistory[0].TextContent);
        Assert.Empty(fixture.ViewModel.AvailableModes);
        Assert.Null(fixture.ViewModel.SelectedMode);
        Assert.Empty(fixture.ViewModel.ConfigOptions);
    }

    [Fact]
    public async Task RestoreAsync_RemoteBoundLastActiveConversation_AutoConnectsRestoredProfile()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument
            {
                LastActiveConversationId = "session-1",
                Conversations =
                {
                    new ConversationRecord
                    {
                        ConversationId = "session-1",
                        DisplayName = "Imported Session",
                        CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedAt = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                        RemoteSessionId = "remote-1",
                        BoundProfileId = "profile-a"
                    }
                }
            });

        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(s => s.ListConfigurationsAsync())
            .ReturnsAsync(Array.Empty<ServerConfiguration>());
        configurationService.Setup(s => s.LoadConfigurationAsync("profile-a"))
            .ReturnsAsync(new ServerConfiguration
            {
                Id = "profile-a",
                Name = "Restored Profile",
                Transport = TransportType.Stdio,
                StdioCommand = "agent.exe"
            });

        var commands = new Mock<IAcpConnectionCommands>();
        commands.Setup(s => s.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpTransportApplyResult(Mock.Of<IChatService>(), new InitializeResponse()));

        await using var fixture = CreateViewModel(
            syncContext,
            conversationStore: conversationStore,
            acpConnectionCommands: commands.Object,
            configurationService: configurationService,
            sessionManager: sessionManager);

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());
        syncContext.RunAll();

        commands.Verify(
            s => s.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-a", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Dispose_DoesNotStartWorkspacePersistenceOwnedByViewModel()
    {
        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument());

        await using var fixture = CreateViewModel(conversationStore: conversationStore);

        fixture.ViewModel.Dispose();
        await Task.Delay(100);

        conversationStore.Verify(
            s => s.SaveAsync(It.IsAny<ConversationDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateNewSessionCommand_BindsRemoteSessionToActivatedLocalConversation()
    {
        await using var fixture = CreateViewModel();
        fixture.Preferences.LastSelectedServerId = "profile-1";

        var chatService = CreateConnectedChatService();
        chatService.Setup(s => s.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-1"));
        await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

        await fixture.ViewModel.CreateNewSessionCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.CurrentSessionId));
        Assert.NotEqual("remote-1", fixture.ViewModel.CurrentSessionId);
        Assert.Equal("remote-1", fixture.ViewModel.CurrentRemoteSessionId);

        var state = await fixture.GetStateAsync();
        Assert.Equal(fixture.ViewModel.CurrentSessionId, state.HydratedConversationId);
        Assert.Equal(
            "remote-1",
            state.ResolveBinding(fixture.ViewModel.CurrentSessionId!)?.RemoteSessionId);

        var remoteBinding = fixture.Workspace.GetRemoteBinding(fixture.ViewModel.CurrentSessionId!);
        Assert.Equal("remote-1", remoteBinding?.RemoteSessionId);
        Assert.Contains(fixture.ViewModel.CurrentSessionId!, fixture.Workspace.GetKnownConversationIds());
    }

    [Fact]
    public async Task InitializeAndConnectCommand_UsesSharedDefaultInitializeRequest()
    {
        await using var fixture = CreateViewModel();
        var chatService = CreateConnectedChatService();

        chatService
            .Setup(service => service.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(
                1,
                new AgentInfo("agent", "1.0.0"),
                new AgentCapabilities()));

        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.ViewModel.InitializeAndConnectCommand.ExecuteAsync(null);

        chatService.Verify(service => service.InitializeAsync(It.Is<InitializeParams>(parameters =>
            parameters.ProtocolVersion == 1
            && string.Equals(parameters.ClientInfo.Name, "SalmonEgg", StringComparison.Ordinal)
            && string.Equals(parameters.ClientInfo.Title, "SalmonEgg", StringComparison.Ordinal)
            && string.Equals(parameters.ClientInfo.Version, "1.0.0", StringComparison.Ordinal)
            && parameters.ClientCapabilities.Terminal == null
            && parameters.ClientCapabilities.Fs == null
            && parameters.ClientCapabilities.SupportsExtension(ClientCapabilityMetadata.AskUserExtensionMethod))),
            Times.Once);
    }

    [Fact]
    public async Task InitializeAndConnectCommand_DrivesStoreBackedInitializingThenConnectedLifecycle()
    {
        var syncContext = new QueueingSynchronizationContext();
        var initializeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var initializeRelease = new TaskCompletionSource<InitializeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = CreateViewModel(
            syncContext,
            acpConnectionCoordinatorFactory: store => new AcpConnectionCoordinator(
                store,
                Mock.Of<ILogger<AcpConnectionCoordinator>>()));
        var chatService = CreateConnectedChatService();

        chatService
            .Setup(service => service.InitializeAsync(It.IsAny<InitializeParams>()))
            .Returns(async () =>
            {
                initializeStarted.TrySetResult(true);
                return await initializeRelease.Task;
            });

        fixture.ViewModel.ReplaceChatService(chatService.Object);
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        syncContext.RunAll();

        var commandTask = fixture.ViewModel.InitializeAndConnectCommand.ExecuteAsync(null);
        await initializeStarted.Task;

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var connectionState = await fixture.GetConnectionStateAsync();
            return connectionState.Phase == ConnectionPhase.Initializing
                && string.Equals(connectionState.SelectedProfileId, "profile-1", StringComparison.Ordinal)
                && fixture.ViewModel.IsInitializing
                && !fixture.ViewModel.IsConnected;
        });

        initializeRelease.SetResult(new InitializeResponse(
            1,
            new AgentInfo("agent", "1.0.0"),
            new AgentCapabilities()));

        await syncContext.RunUntilCompletedAsync(commandTask);
        syncContext.RunAll();

        var finalConnectionState = await fixture.GetConnectionStateAsync();
        Assert.Equal(ConnectionPhase.Connected, finalConnectionState.Phase);
        Assert.Equal("profile-1", finalConnectionState.SelectedProfileId);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                !fixture.ViewModel.IsInitializing
                && fixture.ViewModel.IsConnected
                && !fixture.ViewModel.HasConnectionError
                && string.Equals(fixture.ViewModel.CurrentConnectionStatus, "Connected", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task InitializeAndConnectCommand_Failure_TransitionsToDisconnectedStoreError()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(
            syncContext,
            acpConnectionCoordinatorFactory: store => new AcpConnectionCoordinator(
                store,
                Mock.Of<ILogger<AcpConnectionCoordinator>>()));
        var chatService = CreateConnectedChatService();

        chatService
            .Setup(service => service.InitializeAsync(It.IsAny<InitializeParams>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        fixture.ViewModel.ReplaceChatService(chatService.Object);
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        syncContext.RunAll();

        await fixture.ViewModel.InitializeAndConnectCommand.ExecuteAsync(null);
        syncContext.RunAll();

        var connectionState = await fixture.GetConnectionStateAsync();
        Assert.Equal(ConnectionPhase.Disconnected, connectionState.Phase);
        Assert.Equal("profile-1", connectionState.SelectedProfileId);
        Assert.Equal("boom", connectionState.Error);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                !fixture.ViewModel.IsInitializing
                && !fixture.ViewModel.IsConnected
                && fixture.ViewModel.HasConnectionError
                && string.Equals(fixture.ViewModel.ConnectionErrorMessage, "boom", StringComparison.Ordinal)
                && string.Equals(fixture.ViewModel.CurrentConnectionStatus, "Disconnected", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task CreateNewSessionCommand_LiveUpdatesFromCreatedRemoteSession_ProjectIntoMessageHistory()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        fixture.Preferences.LastSelectedServerId = "profile-1";

        var chatService = CreateConnectedChatService();
        chatService.Setup(s => s.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-1"));
        await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

        await fixture.ViewModel.CreateNewSessionCommand.ExecuteAsync(null);

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new AgentMessageUpdate(new TextContentBlock("hello after create"))));

        await WaitForConditionAsync(() =>
        {
            return Task.FromResult(fixture.ViewModel.MessageHistory.Any(message =>
                !message.IsOutgoing
                && string.Equals(message.TextContent, "hello after create", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public async Task CreateNewSessionCommand_ProjectsOnlyHydratedConversationState()
    {
        await using var fixture = CreateViewModel();
        fixture.Preferences.LastSelectedServerId = "profile-1";

        var chatService = new Mock<IChatService>();
        chatService.Setup(s => s.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-1"));
        await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

        await fixture.ViewModel.CreateNewSessionCommand.ExecuteAsync(null);

        Assert.NotNull((await fixture.GetStateAsync()).HydratedConversationId);
    }

    [Fact]
    public async Task CreateNewSessionCommand_WritesHydratedConversationOnly()
    {
        await using var fixture = CreateViewModel();
        var chatService = new Mock<IChatService>();
        chatService.Setup(s => s.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-1"));
        await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

        await fixture.ViewModel.CreateNewSessionCommand.ExecuteAsync(null);

        var state = await fixture.GetStateAsync();
        Assert.NotNull(state.HydratedConversationId);
    }

    [Fact]
    public async Task MissingHydratedConversation_KeepsCurrentSessionProjectionEmpty()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        syncContext.RunAll();

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = null });
        await Task.Delay(50);
        syncContext.RunAll();

        var state = await fixture.GetStateAsync();
        Assert.Null(state.HydratedConversationId);
        Assert.Null(fixture.ViewModel.CurrentSessionId);
    }

    [Fact]
    public async Task CurrentSessionProjection_InitializesBottomPanelTabs()
    {
        await using var fixture = CreateViewModel();

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await Task.Delay(50);

        Assert.Collection(
            fixture.ViewModel.BottomPanelTabs,
            terminal => Assert.Equal("terminal", terminal.Id),
            output => Assert.Equal("output", output.Id));
        Assert.Equal("terminal", fixture.ViewModel.SelectedBottomPanelTab?.Id);
    }

    [Fact]
    public async Task BottomPanelSelection_PersistsPerConversation()
    {
        await using var fixture = CreateViewModel();

        Assert.Empty(fixture.ViewModel.BottomPanelTabs);
        Assert.Null(fixture.ViewModel.SelectedBottomPanelTab);

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await Task.Delay(50);

        Assert.Equal(2, fixture.ViewModel.BottomPanelTabs.Count);
        var outputTab = fixture.ViewModel.BottomPanelTabs[1];
        Assert.Equal("output", outputTab.Id);

        fixture.ViewModel.SelectedBottomPanelTab = outputTab;
        await Task.Delay(50);

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-2" });
        await Task.Delay(50);

        Assert.Equal("terminal", fixture.ViewModel.SelectedBottomPanelTab?.Id);

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await Task.Delay(50);

        Assert.Equal("output", fixture.ViewModel.SelectedBottomPanelTab?.Id);
    }

    [Fact]
    public async Task ArchiveConversation_CurrentSession_ClearsBottomPanelStateAndDoesNotReviveSelection()
    {
        var activation = new Mock<IConversationActivationCoordinator>();
        activation
            .Setup(a => a.ArchiveConversationAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationMutationResult(true, true, null));

        await using var fixture = CreateViewModel(conversationActivationCoordinator: activation.Object);

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await Task.Delay(50);

        var stateMap = GetBottomPanelStateMap(fixture.ViewModel);
        Assert.True(stateMap.Contains("session-1"));

        var outputTab = fixture.ViewModel.BottomPanelTabs[1];
        Assert.Equal("output", outputTab.Id);
        fixture.ViewModel.SelectedBottomPanelTab = outputTab;
        await Task.Delay(50);
        Assert.Equal("output", fixture.ViewModel.SelectedBottomPanelTab?.Id);

        fixture.ViewModel.ArchiveConversation("session-1");

        activation.Verify(a => a.ArchiveConversationAsync("session-1", "session-1", It.IsAny<CancellationToken>()), Times.Once);

        Assert.Empty(fixture.ViewModel.BottomPanelTabs);
        Assert.Null(fixture.ViewModel.SelectedBottomPanelTab);
        Assert.False(stateMap.Contains("session-1"));

        // If store selection ever toggles away and back, previous selection must not revive.
        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-2" });
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "session-2", StringComparison.Ordinal)));
        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "session-1", StringComparison.Ordinal)
                && string.Equals(fixture.ViewModel.SelectedBottomPanelTab?.Id, "terminal", StringComparison.Ordinal)));

        Assert.Equal("terminal", fixture.ViewModel.SelectedBottomPanelTab?.Id);
    }

    [Fact]
    public async Task DeleteConversation_CurrentSession_ClearsBottomPanelStateAndDoesNotReviveSelection()
    {
        var activation = new Mock<IConversationActivationCoordinator>();
        activation
            .Setup(a => a.DeleteConversationAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationMutationResult(true, true, null));

        await using var fixture = CreateViewModel(conversationActivationCoordinator: activation.Object);

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await Task.Delay(50);

        var stateMap = GetBottomPanelStateMap(fixture.ViewModel);
        Assert.True(stateMap.Contains("session-1"));

        var outputTab = fixture.ViewModel.BottomPanelTabs[1];
        Assert.Equal("output", outputTab.Id);
        fixture.ViewModel.SelectedBottomPanelTab = outputTab;
        await Task.Delay(50);
        Assert.Equal("output", fixture.ViewModel.SelectedBottomPanelTab?.Id);

        fixture.ViewModel.DeleteConversation("session-1");

        activation.Verify(a => a.DeleteConversationAsync("session-1", "session-1", It.IsAny<CancellationToken>()), Times.Once);

        Assert.Empty(fixture.ViewModel.BottomPanelTabs);
        Assert.Null(fixture.ViewModel.SelectedBottomPanelTab);
        Assert.False(stateMap.Contains("session-1"));

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-2" });
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "session-2", StringComparison.Ordinal)));
        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "session-1", StringComparison.Ordinal)
                && string.Equals(fixture.ViewModel.SelectedBottomPanelTab?.Id, "terminal", StringComparison.Ordinal)));

        Assert.Equal("terminal", fixture.ViewModel.SelectedBottomPanelTab?.Id);
    }

    private static IDictionary GetBottomPanelStateMap(ChatViewModel viewModel)
    {
        var field = typeof(ChatViewModel).GetField("_bottomPanelStateByConversation", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field.GetValue(viewModel);
        Assert.NotNull(value);
        return Assert.IsAssignableFrom<IDictionary>(value);
    }

    [Fact]
    public async Task ConnectionProjection_DrivesIsInitialized()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        syncContext.RunAll();

        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        await Task.Delay(50);
        syncContext.RunAll();

        Assert.True(fixture.ViewModel.IsConnected);
        Assert.False(fixture.ViewModel.IsInitializing);
        Assert.True(fixture.ViewModel.IsInitialized);
    }

    [Fact]
    public async Task ConnectToAcpProfileCommand_DoesNotDispatchLegacyConnectionActions()
    {
        var commands = new Mock<IAcpConnectionCommands>();
        var chatService = new Mock<IChatService>();
        chatService.SetupGet(s => s.IsConnected).Returns(true);
        chatService.SetupGet(s => s.IsInitialized).Returns(true);
        commands
            .Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpTransportApplyResult(chatService.Object, new InitializeResponse()));

        await using var fixture = CreateViewModel(acpConnectionCommands: commands.Object);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Profile 1",
            Transport = TransportType.Stdio
        };
        fixture.Profiles.Profiles.Add(profile);

        await fixture.ViewModel.ConnectToAcpProfileCommand.ExecuteAsync(profile);

        AssertNoLegacyConnectionActionsDispatched(fixture.ChatStore);
    }

    [Fact]
    public async Task CoordinatorSinkStateUpdates_DoNotDispatchLegacyConnectionActions()
    {
        await using var fixture = CreateViewModel();

        fixture.ViewModel.UpdateConnectionState(
            isConnecting: true,
            isConnected: false,
            isInitialized: false,
            errorMessage: "connection error");
        fixture.ViewModel.UpdateInitializationState(isInitializing: true);
        fixture.ViewModel.UpdateAuthenticationState(isRequired: true, hintMessage: "auth required");
        await Task.Delay(50);

        AssertNoLegacyConnectionActionsDispatched(fixture.ChatStore);
    }

    [Fact]
    public async Task ArchiveConversation_CurrentSession_ClearsStoreSelection()
    {
        await using var fixture = CreateViewModel();
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await Task.Delay(50);

        fixture.ViewModel.ArchiveConversation("session-1");
        await Task.Delay(50);

        var state = await fixture.GetStateAsync();
        Assert.Null(fixture.ViewModel.CurrentSessionId);
        Assert.False(fixture.ViewModel.IsSessionActive);
        Assert.Null(state.HydratedConversationId);
    }

    [Fact]
    public async Task DeleteConversation_CurrentSession_ClearsStoreSelection()
    {
        await using var fixture = CreateViewModel();
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await Task.Delay(50);

        fixture.ViewModel.DeleteConversation("session-1");
        await Task.Delay(50);

        var state = await fixture.GetStateAsync();
        Assert.Null(fixture.ViewModel.CurrentSessionId);
        Assert.False(fixture.ViewModel.IsSessionActive);
        Assert.Null(state.HydratedConversationId);
    }

    [Fact]
    public async Task SelectedAcpProfile_Change_QueuesNextConnectAttemptWithoutConcurrentOverlap()
    {
        var firstStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectCalls = 0;
        var commands = new Mock<IAcpConnectionCommands>();
        commands
            .Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, CancellationToken>(async (_, _, _, _) =>
            {
                var callNumber = Interlocked.Increment(ref connectCalls);
                if (callNumber == 1)
                {
                    firstStarted.TrySetResult(null);
                    await firstGate.Task;
                }
                else
                {
                    secondStarted.TrySetResult(null);
                }

                return new AcpTransportApplyResult(Mock.Of<IChatService>(), new InitializeResponse());
            });

        await using var fixture = CreateViewModel(acpConnectionCommands: commands.Object);
        var profileA = new ServerConfiguration { Id = "profile-a", Name = "Profile A", Transport = TransportType.Stdio };
        var profileB = new ServerConfiguration { Id = "profile-b", Name = "Profile B", Transport = TransportType.Stdio };
        fixture.Profiles.Profiles.Add(profileA);
        fixture.Profiles.Profiles.Add(profileB);

        fixture.ViewModel.SelectedAcpProfile = profileA;
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        fixture.ViewModel.SelectedAcpProfile = profileB;
        await Task.Delay(100);

        Assert.Equal(1, connectCalls);
        firstGate.TrySetResult(null);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, connectCalls);
    }

    [Fact]
    public async Task SelectedAcpProfile_Change_UpdatesConnectionStoreSelection()
    {
        await using var fixture = CreateViewModel();
        var profile = new ServerConfiguration { Id = "profile-a", Name = "Profile A", Transport = TransportType.Stdio };
        fixture.Profiles.Profiles.Add(profile);

        fixture.ViewModel.SelectedAcpProfile = profile;
        await Task.Delay(50);

        var connectionState = await fixture.GetConnectionStateAsync();
        Assert.Equal("profile-a", connectionState.SelectedProfileId);
    }

    [Fact]
    public async Task SelectProfile_WhenProfileIsNotLoaded_PreservesStoreSelectionWithoutProjectingDetachedInstance()
    {
        await using var fixture = CreateViewModel();
        var detachedProfile = new ServerConfiguration { Id = "profile-a", Name = "Profile A", Transport = TransportType.Stdio };

        fixture.ViewModel.SelectProfile(detachedProfile);
        await Task.Delay(50);

        var connectionState = await fixture.GetConnectionStateAsync();
        Assert.Equal("profile-a", connectionState.SelectedProfileId);
        Assert.Null(fixture.ViewModel.SelectedAcpProfile);
        Assert.Null(fixture.Profiles.SelectedProfile);
    }

    [Fact]
    public async Task CurrentAgentDisplayText_PrefersProtocolAgentName_ThenProfileName_ThenDash()
    {
        await using var fixture = CreateViewModel();

        Assert.Equal("—", fixture.ViewModel.CurrentAgentDisplayText);

        var profile = new ServerConfiguration { Id = "profile-a", Name = "Configured Agent", Transport = TransportType.Stdio };
        fixture.Profiles.Profiles.Add(profile);
        await fixture.ViewModel.SelectProfileAsync(profile);

        Assert.Equal("Configured Agent", fixture.ViewModel.CurrentAgentDisplayText);

        await fixture.UpdateStateAsync(state => state with { AgentName = "Protocol Agent" });
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.CurrentAgentDisplayText, "Protocol Agent", StringComparison.Ordinal)));

        Assert.Equal("Protocol Agent", fixture.ViewModel.CurrentAgentDisplayText);
    }

    [Fact]
    public async Task SelectedAcpProfile_Change_PreservesActiveRemoteBindingUntilReconnectCompletes()
    {
        var connectGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new Mock<IAcpConnectionCommands>();
        commands
            .Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, CancellationToken>(async (_, _, _, _) =>
            {
                connectStarted.TrySetResult(null);
                await connectGate.Task;
                return new AcpTransportApplyResult(Mock.Of<IChatService>(), new InitializeResponse());
            });

        await using var fixture = CreateViewModel(acpConnectionCommands: commands.Object);
        var profileA = new ServerConfiguration { Id = "profile-a", Name = "Profile A", Transport = TransportType.Stdio };
        var profileB = new ServerConfiguration { Id = "profile-b", Name = "Profile B", Transport = TransportType.Stdio };
        fixture.Profiles.Profiles.Add(profileA);
        fixture.Profiles.Profiles.Add(profileB);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-1", "profile-a"))
        });
        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            return state.ResolveBinding("session-1") == new ConversationBindingSlice("session-1", "remote-1", "profile-a");
        });

        fixture.ViewModel.SelectedAcpProfile = profileB;
        await connectStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            return state.ResolveBinding("session-1") == new ConversationBindingSlice("session-1", "remote-1", "profile-a")
                && string.Equals(fixture.ViewModel.CurrentRemoteSessionId, "remote-1", StringComparison.Ordinal);
        });

        var state = await fixture.GetStateAsync();
        Assert.Equal(new ConversationBindingSlice("session-1", "remote-1", "profile-a"), state.ResolveBinding("session-1"));
        Assert.Equal("remote-1", fixture.ViewModel.CurrentRemoteSessionId);

        connectGate.TrySetResult(null);
    }

    [Fact]
    public async Task SelectedAcpProfile_Change_WhenReconnectFails_PreservesActiveRemoteBinding()
    {
        var commands = new Mock<IAcpConnectionCommands>();
        commands
            .Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("profile switch failed"));

        await using var fixture = CreateViewModel(acpConnectionCommands: commands.Object);
        var profileA = new ServerConfiguration { Id = "profile-a", Name = "Profile A", Transport = TransportType.Stdio };
        var profileB = new ServerConfiguration { Id = "profile-b", Name = "Profile B", Transport = TransportType.Stdio };
        fixture.Profiles.Profiles.Add(profileA);
        fixture.Profiles.Profiles.Add(profileB);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-1", "profile-a"))
        });
        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            return state.ResolveBinding("session-1") == new ConversationBindingSlice("session-1", "remote-1", "profile-a");
        });

        fixture.ViewModel.SelectedAcpProfile = profileB;
        await WaitForConditionAsync(() =>
            Task.FromResult(!string.IsNullOrWhiteSpace(fixture.ViewModel.ConnectionErrorMessage)));

        var state = await fixture.GetStateAsync();
        Assert.Equal(new ConversationBindingSlice("session-1", "remote-1", "profile-a"), state.ResolveBinding("session-1"));
        Assert.Equal("remote-1", fixture.ViewModel.CurrentRemoteSessionId);
        Assert.Contains("profile switch failed", fixture.ViewModel.ConnectionErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectedAcpProfile_Change_SurfacesConnectionFailure()
    {
        var commands = new Mock<IAcpConnectionCommands>();
        commands
            .Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("profile switch failed"));

        await using var fixture = CreateViewModel(acpConnectionCommands: commands.Object);
        var profile = new ServerConfiguration { Id = "profile-a", Name = "Profile A", Transport = TransportType.Stdio };
        fixture.Profiles.Profiles.Add(profile);

        fixture.ViewModel.SelectedAcpProfile = profile;
        await Task.Delay(100);

        Assert.Contains("profile switch failed", fixture.ViewModel.ConnectionErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectedProfileId_DoesNotFallBackToUiSelection_WhenStoreSelectionIsMissing()
    {
        await using var fixture = CreateViewModel();
        var profile = new ServerConfiguration { Id = "profile-a", Name = "Profile A", Transport = TransportType.Stdio };
        fixture.Profiles.Profiles.Add(profile);
        var suppressField = typeof(ChatViewModel)
            .GetField("_suppressStoreProfileProjection", BindingFlags.Instance | BindingFlags.NonPublic);
        var suppressConnectField = typeof(ChatViewModel)
            .GetField("_suppressAcpProfileConnect", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(suppressField);
        Assert.NotNull(suppressConnectField);
        suppressField!.SetValue(fixture.ViewModel, true);
        suppressConnectField!.SetValue(fixture.ViewModel, true);

        try
        {
            fixture.ViewModel.SelectedAcpProfile = profile;
            await Task.Delay(50);
        }
        finally
        {
            suppressField.SetValue(fixture.ViewModel, false);
            suppressConnectField.SetValue(fixture.ViewModel, false);
        }

        Assert.Null(fixture.ViewModel.SelectedProfileId);
    }

    [Fact]
    public async Task SessionUpdate_AfterBindingChange_UsesStoreBindingInsteadOfStaleProjectedRemoteSessionId()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-new", "profile-a"))
        });
        await Task.Delay(50);
        SetPrivateField(fixture.ViewModel, "_currentRemoteSessionId", "remote-old");

        chatService.Raise(
            x => x.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-new",
                new AgentMessageUpdate(new TextContentBlock { Text = "fresh reply" })));

        await Task.Delay(50);

        var message = Assert.Single(fixture.ViewModel.MessageHistory);
        Assert.Equal("fresh reply", message.TextContent);
    }

    [Fact]
    public async Task SessionUpdate_UnrelatedRemoteSession_IsIgnored_WhenStoreBindingTargetsDifferentSession()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-good", "profile-a"))
        });
        await Task.Delay(50);
        SetPrivateField(fixture.ViewModel, "_currentRemoteSessionId", "remote-stale");

        chatService.Raise(
            x => x.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-other",
                new AgentMessageUpdate(new TextContentBlock { Text = "ignore me" })));

        await Task.Delay(50);

        Assert.Empty(fixture.ViewModel.MessageHistory);
    }

    [Fact]
    public async Task SetModeCommand_UsesStoreBindingInsteadOfStaleProjectedRemoteSessionId()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        chatService
            .Setup(x => x.SetSessionModeAsync(It.IsAny<SessionSetModeParams>()))
            .ReturnsAsync(new SessionSetModeResponse("plan"));
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-fresh", "profile-a"))
        });
        await Task.Delay(50);
        SetPrivateField(fixture.ViewModel, "_currentRemoteSessionId", "remote-stale");

        await fixture.ViewModel.SetModeCommand.ExecuteAsync(new SessionModeViewModel { ModeId = "plan", ModeName = "Plan" });

        chatService.Verify(
            x => x.SetSessionModeAsync(
                It.Is<SessionSetModeParams>(parameters =>
                    parameters.SessionId == "remote-fresh" &&
                    parameters.ModeId == "plan")),
            Times.Once);
    }

    [Fact]
    public async Task CancelSessionCommand_UsesStoreBindingInsteadOfStaleProjectedRemoteSessionId()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        chatService
            .Setup(x => x.CancelSessionAsync(It.IsAny<SessionCancelParams>()))
            .ReturnsAsync(new SessionCancelResponse(true));
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-fresh", "profile-a"))
        });
        await Task.Delay(50);
        SetPrivateField(fixture.ViewModel, "_currentRemoteSessionId", "remote-stale");

        await fixture.ViewModel.CancelSessionCommand.ExecuteAsync(null);

        chatService.Verify(
            x => x.CancelSessionAsync(It.Is<SessionCancelParams>(parameters => parameters.SessionId == "remote-fresh")),
            Times.Once);
    }

    [Fact]
    public async Task TryAutoConnectAsync_CanceledToken_DoesNotStartConnection()
    {
        var commands = new Mock<IAcpConnectionCommands>();
        var configurationService = new Mock<IConfigurationService>();
        await using var fixture = CreateViewModel(
            acpConnectionCommands: commands.Object,
            configurationService: configurationService);
        var lastSelectedServerIdField = typeof(AppPreferencesViewModel)
            .GetField("_lastSelectedServerId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lastSelectedServerIdField);
        lastSelectedServerIdField!.SetValue(fixture.Preferences, "profile-1");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.ViewModel.TryAutoConnectAsync(cts.Token));

        commands.Verify(
            x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        configurationService.Verify(x => x.LoadConfigurationAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TryAutoConnectAsync_CanceledDuringConnect_AllowsLaterRetry()
    {
        var connectCalls = 0;
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new Mock<IAcpConnectionCommands>();
        commands
            .Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, CancellationToken>(async (_, _, _, cancellationToken) =>
            {
                var callNumber = Interlocked.Increment(ref connectCalls);
                if (callNumber == 1)
                {
                    firstStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return new AcpTransportApplyResult(Mock.Of<IChatService>(), new InitializeResponse());
            });

        await using var fixture = CreateViewModel(acpConnectionCommands: commands.Object);
        var profile = new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio };
        fixture.Profiles.Profiles.Add(profile);

        var lastSelectedServerIdField = typeof(AppPreferencesViewModel)
            .GetField("_lastSelectedServerId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lastSelectedServerIdField);
        lastSelectedServerIdField!.SetValue(fixture.Preferences, profile.Id);

        using var firstAttempt = new CancellationTokenSource();
        var firstTask = fixture.ViewModel.TryAutoConnectAsync(firstAttempt.Token);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        firstAttempt.Cancel();

        await firstTask;
        Assert.Equal(1, connectCalls);

        await fixture.ViewModel.TryAutoConnectAsync(CancellationToken.None);

        Assert.Equal(2, connectCalls);
    }

    [Fact]
    public async Task TryAutoConnectAsync_FallbackProfileOutsideLoadedList_DoesNotProjectDetachedUiSelection()
    {
        var commands = new Mock<IAcpConnectionCommands>();
        commands
            .Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpTransportApplyResult(Mock.Of<IChatService>(), new InitializeResponse()));

        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(x => x.ListConfigurationsAsync()).ReturnsAsync(Array.Empty<ServerConfiguration>());
        configurationService.Setup(x => x.LoadConfigurationAsync("profile-1")).ReturnsAsync(
            new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Detached Profile",
                Transport = TransportType.Stdio
            });

        await using var fixture = CreateViewModel(
            acpConnectionCommands: commands.Object,
            configurationService: configurationService);
        var lastSelectedServerIdField = typeof(AppPreferencesViewModel)
            .GetField("_lastSelectedServerId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lastSelectedServerIdField);
        lastSelectedServerIdField!.SetValue(fixture.Preferences, "profile-1");

        await fixture.ViewModel.TryAutoConnectAsync(CancellationToken.None);

        Assert.Null(fixture.ViewModel.SelectedAcpProfile);
        Assert.Null(fixture.Profiles.SelectedProfile);
        commands.Verify(
            x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StoreTranscript_ProjectsToMessageHistory()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        syncContext.RunAll();

        var snapshot = new ConversationMessageSnapshot
        {
            Id = "m-1",
            ContentType = "text",
            TextContent = "hello",
            Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Transcript = ImmutableList<ConversationMessageSnapshot>.Empty.Add(snapshot)
        });

        await Task.Delay(50);
        syncContext.RunAll();

        Assert.Single(fixture.ViewModel.MessageHistory);
        Assert.Equal("hello", fixture.ViewModel.MessageHistory[0].TextContent);
    }

    [Fact]
    public async Task Dispose_CancelsStoreSubscription_DoesNotUpdateAfterDispose()
    {
        var initialState = ChatState.Empty with { ActiveTurn = null };
        var chatStore = new Mock<IChatStore>();
        await using var state = State.Value(this, () => initialState);
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action => state.Update(s => ChatReducer.Reduce(s!, action), CancellationToken.None));

        var syncContext = new QueueingSynchronizationContext();
        var transportFactory = new Mock<ITransportFactory>();
        var messageParser = new Mock<IMessageParser>();
        var messageValidator = new Mock<IMessageValidator>();
        var errorLogger = new Mock<IErrorLogger>();
        var sessionManager = new Mock<ISessionManager>();
        var serilog = new Mock<Serilog.ILogger>();

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            sessionManager.Object,
            serilog.Object);

        var configService = new Mock<IConfigurationService>();
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object);

        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object);
        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument());
        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var workspace = new ChatConversationWorkspace(
            sessionManager.Object,
            conversationStore.Object,
            new AppPreferencesConversationWorkspacePreferences(preferences),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            syncContext);
        var conversationCatalogPresenter = new ConversationCatalogPresenter();
        var vmLogger = new Mock<ILogger<ChatViewModel>>();
        await using var connectionState = State.Value(new object(), () => ChatConnectionState.Empty);
        var connectionStore = new ChatConnectionStore(connectionState);
        var chatStateProjector = new ChatStateProjector();

        using var viewModel = new ChatViewModel(
            chatStore.Object,
            chatServiceFactory,
            configService.Object,
            preferences,
            profiles,
            sessionManager.Object,
            miniWindow.Object,
            workspace,
            conversationCatalogPresenter,
            chatStateProjector,
            null,
            connectionStore,
            vmLogger.Object,
            syncContext);

        syncContext.RunAll();
        Assert.False(viewModel.IsTurnStatusVisible);

        viewModel.Dispose();

        var newState = initialState with { ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow) };
        await state.Update(_ => newState, CancellationToken.None);

        syncContext.RunAll();
        Assert.False(viewModel.IsTurnStatusVisible);
    }

    [Fact]
    public async Task Dispose_DropsAlreadyQueuedStoreProjection()
    {
        var initialState = ChatState.Empty with { ActiveTurn = null };
        await using var state = State.Value(this, () => initialState);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action => state.Update(s => ChatReducer.Reduce(s!, action), CancellationToken.None));

        var syncContext = new QueueingSynchronizationContext();
        var transportFactory = new Mock<ITransportFactory>();
        var messageParser = new Mock<IMessageParser>();
        var messageValidator = new Mock<IMessageValidator>();
        var errorLogger = new Mock<IErrorLogger>();
        var sessionManager = new Mock<ISessionManager>();
        var serilog = new Mock<Serilog.ILogger>();

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            sessionManager.Object,
            serilog.Object);

        var configService = new Mock<IConfigurationService>();
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object);

        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object);
        var conversationStore = new Mock<IConversationStore>();
        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var workspace = new ChatConversationWorkspace(
            sessionManager.Object,
            conversationStore.Object,
            new AppPreferencesConversationWorkspacePreferences(preferences),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            syncContext);
        var conversationCatalogPresenter = new ConversationCatalogPresenter();
        var vmLogger = new Mock<ILogger<ChatViewModel>>();
        await using var connectionState = State.Value(new object(), () => ChatConnectionState.Empty);
        var connectionStore = new ChatConnectionStore(connectionState);
        var chatStateProjector = new ChatStateProjector();

        using var viewModel = new ChatViewModel(
            chatStore.Object,
            chatServiceFactory,
            configService.Object,
            preferences,
            profiles,
            sessionManager.Object,
            miniWindow.Object,
            workspace,
            conversationCatalogPresenter,
            chatStateProjector,
            null,
            connectionStore,
            vmLogger.Object,
            syncContext);

        syncContext.RunAll();
        Assert.False(viewModel.IsTurnStatusVisible);

        await state.Update(_ => initialState with { ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow) }, CancellationToken.None);
        viewModel.Dispose();

        syncContext.RunAll();
        Assert.False(viewModel.IsTurnStatusVisible);
    }

    [Fact]
    public async Task Dispose_DoesNotDisposeInjectedConversationWorkspace()
    {
        var syncContext = new SynchronizationContext();
        var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action => state.Update(s => ChatReducer.Reduce(s!, action), default));

        var transportFactory = new Mock<ITransportFactory>();
        var messageParser = new Mock<IMessageParser>();
        var messageValidator = new Mock<IMessageValidator>();
        var errorLogger = new Mock<IErrorLogger>();
        var serilog = new Mock<SerilogLogger>();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((sessionId, cwd) =>
            {
                var session = new Session(sessionId, cwd);
                sessions[sessionId] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(sessionId => sessions.TryGetValue(sessionId, out var session) ? session : null);
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((sessionId, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(sessionId, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(sessionId => sessions.Remove(sessionId));

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            sessionManager.Object,
            serilog.Object);

        var configService = new Mock<IConfigurationService>();
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object);

        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object);
        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument());
        var workspace = new ChatConversationWorkspace(
            sessionManager.Object,
            conversationStore.Object,
            new AppPreferencesConversationWorkspacePreferences(preferences),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            syncContext);
        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var conversationCatalogPresenter = new ConversationCatalogPresenter();
        var vmLogger = new Mock<ILogger<ChatViewModel>>();
        await using var connectionState = State.Value(new object(), () => ChatConnectionState.Empty);
        var connectionStore = new ChatConnectionStore(connectionState);
        var chatStateProjector = new ChatStateProjector();

        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(syncContext);
            using var viewModel = new ChatViewModel(
                chatStore.Object,
                chatServiceFactory,
                configService.Object,
                preferences,
                profiles,
                sessionManager.Object,
                miniWindow.Object,
                workspace,
                conversationCatalogPresenter,
                chatStateProjector,
                null,
                connectionStore,
                vmLogger.Object,
                syncContext);

            viewModel.Dispose();

            var switched = await workspace.TrySwitchToSessionAsync("session-1");
            Assert.True(switched);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
            await state.DisposeAsync();
            workspace.Dispose();
        }
    }

    [Fact]
    public async Task CurrentPrompt_UpdatesDraftTextInStore()
    {
        await using var fixture = CreateViewModel();
        var viewModel = fixture.ViewModel;

        viewModel.CurrentPrompt = "draft text";

        await Task.Delay(50);

        Assert.Equal("draft text", viewModel.CurrentPrompt);
        Assert.Equal("draft text", (await fixture.GetStateAsync()).DraftText);
    }

    [Fact]
    public async Task StoreDraftText_ProjectsToCurrentPrompt()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        syncContext.RunAll();

        await fixture.DispatchAsync(new SetDraftTextAction("from store"));
        await Task.Delay(50);
        syncContext.RunAll();

        Assert.Equal("from store", viewModel.CurrentPrompt);
    }

    [Fact]
    public async Task PlanEntries_CollectionChanges_RaiseDerivedPropertyNotifications()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var raised = new List<string>();
        syncContext.RunAll();

        viewModel.ShowPlanPanel = true;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                raised.Add(e.PropertyName!);
            }
        };

        viewModel.PlanEntries.Add(new PlanEntryViewModel
        {
            Content = "Step 1"
        });

        await Task.Yield();

        Assert.Contains(nameof(ChatViewModel.HasPlanEntries), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldShowPlanList), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldShowPlanEmpty), raised);
        Assert.True(viewModel.HasPlanEntries);
        Assert.True(viewModel.ShouldShowPlanList);
        Assert.False(viewModel.ShouldShowPlanEmpty);
    }

    private sealed class QueueingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback callback, object? state)> _work = new();
        private readonly object _gate = new();

        public int PendingCount
        {
            get
            {
                lock (_gate)
                {
                    return _work.Count;
                }
            }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (d is null)
            {
                return;
            }

            lock (_gate)
            {
                _work.Enqueue((d, state));
            }
        }

        public bool RunNext()
        {
            SendOrPostCallback? callback;
            object? state;
            lock (_gate)
            {
                if (_work.Count == 0)
                {
                    return false;
                }

                (callback, state) = _work.Dequeue();
            }

            try
            {
                var originalContext = Current;
                try
                {
                    SetSynchronizationContext(this);
                    callback(state);
                }
                finally
                {
                    SetSynchronizationContext(originalContext);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Queued synchronization callback failed: {callback?.Method.DeclaringType?.FullName}.{callback?.Method.Name}",
                    ex);
            }

            return true;
        }

        public void RunAll()
        {
            while (RunNext())
            {
            }
        }

        public async Task RunUntilCompletedAsync(Task task, int spinDelayMs = 10)
        {
            while (!task.IsCompleted)
            {
                if (PendingCount == 0)
                {
                    await Task.Delay(spinDelayMs);
                    continue;
                }

                RunAll();
            }

            await task;
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private static Task AwaitWithSynchronizationContextAsync(SynchronizationContext syncContext, Task task)
        => syncContext is QueueingSynchronizationContext queueingContext
            ? queueingContext.RunUntilCompletedAsync(task)
            : task;

    private static void AssertNoLegacyConnectionActionsDispatched(RecordingChatStore chatStore)
    {
        var dispatchedActions = chatStore.Actions;

        var legacyActionTypeNames = new[]
        {
            "SetConnectionLifecycleAction",
            "UpdateConnectionStatusAction",
            "SetAuthenticationStateAction"
        };

        Assert.DoesNotContain(dispatchedActions, action =>
            legacyActionTypeNames.Contains(action.GetType().Name, StringComparer.Ordinal));
    }

    private static async Task WaitForConditionAsync(
        Func<Task<bool>> predicate,
        int timeoutMilliseconds = 4000,
        int pollDelayMilliseconds = 10)
    {
        var timeoutAt = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (await predicate().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(pollDelayMilliseconds).ConfigureAwait(false);
        }

        Assert.True(await predicate().ConfigureAwait(false), "Timed out waiting for expected asynchronous condition.");
    }

    private static Mock<IChatService> CreateConnectedChatService()
    {
        var chatService = new Mock<IChatService>();
        chatService.SetupGet(service => service.IsConnected).Returns(true);
        chatService.SetupGet(service => service.IsInitialized).Returns(true);
        chatService.SetupGet(service => service.SessionHistory).Returns(Array.Empty<SessionUpdateEntry>());
        return chatService;
    }

    private sealed class ReplayLoadChatService : IChatService
    {
        public string? CurrentSessionId { get; private set; }

        public bool IsInitialized => true;

        public bool IsConnected => true;

        public AgentInfo? AgentInfo => null;

        public AgentCapabilities? AgentCapabilities { get; init; }

        public IReadOnlyList<SessionUpdateEntry> SessionHistory => Array.Empty<SessionUpdateEntry>();

        public Plan? CurrentPlan => null;

        public SessionModeState? CurrentMode => null;

        public Func<SessionLoadParams, CancellationToken, Task<SessionLoadResponse>>? OnLoadSessionAsync { get; init; }

        public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived;

        public event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<AskUserRequestEventArgs>? AskUserRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<string>? ErrorOccurred
        {
            add { }
            remove { }
        }

        public void RaiseSessionUpdate(SessionUpdateEventArgs args)
            => SessionUpdateReceived?.Invoke(this, args);

        public Task<InitializeResponse> InitializeAsync(InitializeParams @params)
            => throw new NotSupportedException();

        public Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params)
            => throw new NotSupportedException();

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params)
            => LoadSessionAsync(@params, CancellationToken.None);

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken)
        {
            CurrentSessionId = @params.SessionId;
            return OnLoadSessionAsync?.Invoke(@params, cancellationToken) ?? Task.FromResult(SessionLoadResponse.Completed);
        }

        public Task<SessionListResponse> ListSessionsAsync(SessionListParams? @params = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params)
            => throw new NotSupportedException();

        public Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params)
            => throw new NotSupportedException();

        public Task<SessionCancelResponse> CancelSessionAsync(SessionCancelParams @params)
            => throw new NotSupportedException();

        public Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null)
            => throw new NotSupportedException();

        public Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
            => throw new NotSupportedException();

        public Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers)
            => throw new NotSupportedException();

        public Task<bool> DisconnectAsync()
            => throw new NotSupportedException();

        public Task<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?> GetAvailableModesAsync()
            => throw new NotSupportedException();

        public void ClearHistory()
        {
        }
    }

    private static List<ConfigOption> CreateModeConfigOptions(string currentValue)
        => new()
        {
            new ConfigOption
            {
                Id = "mode",
                Name = "Mode",
                Category = "mode",
                Type = "select",
                CurrentValue = currentValue,
                Options = new List<ConfigOptionValue>
                {
                    new() { Value = "agent", Name = "Agent", Description = "Default agent mode" },
                    new() { Value = "plan", Name = "Plan", Description = "Planning mode" }
                }
            }
        };

    private static List<ConfigOption> CreateNonCanonicalModeConfigOptions(string currentValue)
        => new()
        {
            new ConfigOption
            {
                Id = "_salmonloop_permission_policy",
                Name = "Permission policy",
                Type = "select",
                CurrentValue = "ask",
                Options = new List<ConfigOptionValue>
                {
                    new() { Value = "ask", Name = "Ask user" },
                    new() { Value = "deny_all", Name = "Deny all" }
                }
            },
            new ConfigOption
            {
                Id = "_salmonloop_mode",
                Name = "Session Mode",
                Type = "select",
                CurrentValue = currentValue,
                Options = new List<ConfigOptionValue>
                {
                    new() { Value = "interactive", Name = "Interactive", Description = "Default interactive mode" },
                    new() { Value = "yolo", Name = "YOLO", Description = "Aggressive mode" }
                }
            }
        };

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static async Task InvokePrivateTaskAsync(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = method!.Invoke(instance, arguments);
        if (result is Task task)
        {
            await task;
        }
    }

    private static async Task<T> InvokePrivateTaskAsync<T>(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = method!.Invoke(instance, arguments);
        var task = Assert.IsAssignableFrom<Task<T>>(result);
        return await task;
    }

    private sealed class ViewModelFixture : IDisposable, IAsyncDisposable
    {
        private readonly IState<ChatState> _state;
        private readonly IState<ChatConnectionState> _connectionState;
        private readonly IChatConnectionStore _connectionStore;
        private readonly IChatStore _store;
        private readonly ChatConversationWorkspace _workspace;
        private readonly RecordingChatStore _chatStore;
        public ChatViewModel ViewModel { get; }
        public Mock<IConversationStore> ConversationStore { get; }
        public ChatConversationWorkspace Workspace => _workspace;
        public AppPreferencesViewModel Preferences { get; }
        public AcpProfilesViewModel Profiles { get; }
        public RecordingChatStore ChatStore => _chatStore;
        public Mock<ILogger<ChatViewModel>> ViewModelLogger { get; }

        public ViewModelFixture(
            ChatViewModel viewModel,
            IState<ChatState> state,
            IState<ChatConnectionState> connectionState,
            IChatConnectionStore connectionStore,
            IChatStore store,
            ChatConversationWorkspace workspace,
            Mock<IConversationStore> conversationStore,
            AppPreferencesViewModel preferences,
            AcpProfilesViewModel profiles,
            RecordingChatStore chatStore,
            Mock<ILogger<ChatViewModel>> viewModelLogger)
        {
            ViewModel = viewModel;
            _state = state;
            _connectionState = connectionState;
            _connectionStore = connectionStore;
            _store = store;
            _workspace = workspace;
            ConversationStore = conversationStore;
            Preferences = preferences;
            Profiles = profiles;
            _chatStore = chatStore;
            ViewModelLogger = viewModelLogger;
        }

        public Task<ChatState> GetStateAsync() => Task.FromResult(_chatStore.LatestState);

        public async Task<ChatConnectionState> GetConnectionStateAsync() => await _connectionState ?? ChatConnectionState.Empty;

        public ValueTask DispatchAsync(ChatAction action) => _store.Dispatch(action);

        public ValueTask DispatchConnectionAsync(ChatConnectionAction action) => _connectionStore.Dispatch(action);

        public ValueTask UpdateStateAsync(Func<ChatState, ChatState> update)
            => _chatStore.SetStateAsync(update(_chatStore.LatestState));

        public async ValueTask DisposeAsync()
        {
            ViewModel.Dispose();
            _workspace.Dispose();
            await _connectionState.DisposeAsync();
            await _state.DisposeAsync();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class RecordingChatStore : IChatStore
    {
        private readonly ConcurrentQueue<ChatAction> _actions = new();
        private readonly SemaphoreSlim _dispatchGate = new(1, 1);

        public RecordingChatStore(IState<ChatState> state)
        {
            State = state;
            LatestState = ChatState.Empty;
        }

        public IState<ChatState> State { get; }
        public ChatState LatestState { get; private set; }
        public Action<ChatAction>? AfterDispatch { get; set; }

        public IReadOnlyCollection<ChatAction> Actions => _actions.ToArray();

        public async ValueTask Dispatch(ChatAction action)
        {
            _actions.Enqueue(action);
            await _dispatchGate.WaitAsync();
            try
            {
                var currentState = LatestState;
                var updatedState = ChatReducer.Reduce(currentState, action);
                LatestState = updatedState;
                await State.Update(_ => updatedState, CancellationToken.None);
                AfterDispatch?.Invoke(action);
            }
            finally
            {
                _dispatchGate.Release();
            }
        }

        public async ValueTask SetStateAsync(ChatState state)
        {
            LatestState = state;
            await State.Update(_ => state, CancellationToken.None);
        }
    }

    [Fact]
    public async Task SendPromptAsync_WithoutBinding_BeginsTurnAsCreatingRemoteSession()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected connect"));
        var tcsSession = new TaskCompletionSource<AcpRemoteSessionResult>();
        var tcsPrompt = new TaskCompletionSource<AcpPromptDispatchResult>();
        
        commands.Setup(x => x.EnsureRemoteSessionAsync(It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns(tcsSession.Task);
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns(tcsPrompt.Task);

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;

        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-1", Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();
        // Wait for projection
        await Task.Delay(50);
        syncContext.RunAll();
        
        Assert.True(viewModel.IsSessionActive);
        viewModel.CurrentPrompt = "hi";
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();
        Assert.True(viewModel.CanSendPromptUi);

        var sendTask = viewModel.SendPromptCommand.ExecuteAsync(null);
        
        // At this point, it should have dispatched BeginTurnAction with CreatingRemoteSession
        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var currentState = await fixture.GetStateAsync();
            return currentState.ActiveTurn?.Phase == ChatTurnPhase.CreatingRemoteSession;
        });

        var state = await fixture.GetStateAsync();
        Assert.NotNull(state.ActiveTurn);
        Assert.Equal("conv-1", state.ActiveTurn!.ConversationId);
        Assert.Equal(ChatTurnPhase.CreatingRemoteSession, state.ActiveTurn.Phase);

        // Complete EnsureRemoteSessionAsync
        tcsSession.SetResult(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));
        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return (await fixture.GetStateAsync()).ActiveTurn?.Phase == ChatTurnPhase.WaitingForAgent;
        });

        state = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.WaitingForAgent, state.ActiveTurn!.Phase);

        // Now complete the prompt dispatch
        tcsPrompt.SetResult(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(), false));
        await sendTask;
        
        // Flush the terminal-state projection that follows the prompt response.
        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return (await fixture.GetStateAsync()).ActiveTurn?.Phase == ChatTurnPhase.Completed;
        });

        state = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.Completed, state.ActiveTurn!.Phase);
    }

    [Fact]
    public async Task SendPromptAsync_WhenRemoteSessionIsCreated_ProjectsSessionModesFromSessionNewResponse()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpRemoteSessionResult(
                "remote-1",
                new SessionNewResponse("remote-1", configOptions: CreateModeConfigOptions("agent")),
                UsedExistingBinding: false));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                "remote-1",
                "show modes",
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(), false));

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        viewModel.ReplaceChatService(CreateConnectedChatService().Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        viewModel.CurrentPrompt = "show modes";
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        await viewModel.SendPromptCommand.ExecuteAsync(null);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                viewModel.AvailableModes.Count == 2
                && viewModel.ConfigOptions.Count == 1
                && string.Equals(viewModel.SelectedMode?.ModeId, "agent", StringComparison.Ordinal));
        });

        Assert.Equal("agent", viewModel.SelectedMode?.ModeId);
        Assert.Contains(viewModel.AvailableModes, mode => string.Equals(mode.ModeId, "plan", StringComparison.Ordinal));
        Assert.Equal("mode", viewModel.ConfigOptions[0].Id);
        Assert.Equal("agent", viewModel.ConfigOptions[0].Value);
    }

    [Fact]
    public async Task SendPromptAsync_WhenSessionNewUsesLegacyModesAndNonCanonicalConfigOptions_StillProjectsModeList()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpRemoteSessionResult(
                "remote-1",
                new SessionNewResponse(
                    "remote-1",
                    modes: new SessionModesState
                    {
                        CurrentModeId = "yolo",
                        AvailableModes = new List<SalmonEgg.Domain.Models.Protocol.SessionMode>
                        {
                            new() { Id = "interactive", Name = "Interactive" },
                            new() { Id = "yolo", Name = "YOLO" }
                        }
                    },
                    configOptions: CreateNonCanonicalModeConfigOptions("yolo")),
                UsedExistingBinding: false));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                "remote-1",
                "show modes",
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(), false));

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        viewModel.ReplaceChatService(CreateConnectedChatService().Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        viewModel.CurrentPrompt = "show modes";
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        await viewModel.SendPromptCommand.ExecuteAsync(null);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                viewModel.AvailableModes.Count == 2
                && string.Equals(viewModel.SelectedMode?.ModeId, "yolo", StringComparison.Ordinal)
                && viewModel.ConfigOptions.Count == 2);
        });

        Assert.Contains(viewModel.AvailableModes, mode => string.Equals(mode.ModeId, "interactive", StringComparison.Ordinal));
        Assert.Contains(viewModel.AvailableModes, mode => string.Equals(mode.ModeId, "yolo", StringComparison.Ordinal));
        Assert.Equal("yolo", viewModel.SelectedMode?.ModeId);
        Assert.Equal(2, viewModel.ConfigOptions.Count);
    }

    [Fact]
    public async Task SendPromptAsync_WhenPromptResponseIsCancelled_PreservesCancelledTurnPhase()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.EnsureRemoteSessionAsync(It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(StopReason.Cancelled), false));

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        viewModel.ReplaceChatService(CreateConnectedChatService().Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        viewModel.CurrentPrompt = "cancel me";
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();
        Assert.True(viewModel.CanSendPromptUi);

        await viewModel.SendPromptCommand.ExecuteAsync(null);
        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return (await fixture.GetStateAsync()).ActiveTurn?.Phase == ChatTurnPhase.Cancelled;
        });

        var state = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.Cancelled, state.ActiveTurn!.Phase);
    }

    [Fact]
    public async Task CancelPromptCommand_PreemptivelyCancelsOutstandingToolCallsForCurrentTurn()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.CancelPromptAsync(It.IsAny<IAcpChatCoordinatorSink>(), "User cancelled"))
            .Returns(Task.CompletedTask);

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        viewModel.ReplaceChatService(CreateConnectedChatService().Object);

        var turnStartedAt = new DateTime(2026, 3, 24, 3, 0, 0, DateTimeKind.Utc);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            Transcript = ImmutableList.Create(
                new ConversationMessageSnapshot
                {
                    Id = "tool-old",
                    Timestamp = turnStartedAt.AddMinutes(-1),
                    ContentType = "tool_call",
                    ToolCallId = "tool-old",
                    ToolCallStatus = ToolCallStatus.InProgress,
                    Title = "old"
                },
                new ConversationMessageSnapshot
                {
                    Id = "tool-current",
                    Timestamp = turnStartedAt.AddSeconds(5),
                    ContentType = "tool_call",
                    ToolCallId = "tool-current",
                    ToolCallStatus = ToolCallStatus.InProgress,
                    Title = "current"
                }),
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.ToolRunning, turnStartedAt, turnStartedAt),
            IsPromptInFlight = true
        });
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        await viewModel.CancelPromptCommand.ExecuteAsync(null);

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var state = await fixture.GetStateAsync();
            var transcript = state.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            return state.ActiveTurn?.Phase == ChatTurnPhase.Cancelled
                && transcript.Single(message => string.Equals(message.Id, "tool-current", StringComparison.Ordinal)).ToolCallStatus == ToolCallStatus.Cancelled;
        });

        var finalState = await fixture.GetStateAsync();
        var finalTranscript = finalState.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.Equal(ChatTurnPhase.Cancelled, finalState.ActiveTurn!.Phase);
        Assert.Equal(ToolCallStatus.InProgress, finalTranscript.Single(message => string.Equals(message.Id, "tool-old", StringComparison.Ordinal)).ToolCallStatus);
        Assert.Equal(ToolCallStatus.Cancelled, finalTranscript.Single(message => string.Equals(message.Id, "tool-current", StringComparison.Ordinal)).ToolCallStatus);

        commands.Verify(x => x.CancelPromptAsync(It.IsAny<IAcpChatCoordinatorSink>(), "User cancelled"), Times.Once);
    }

    [Fact]
    public async Task SendPromptAsync_WhenPromptResponseIsRefusal_PreservesFailedTurnWithReason()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.EnsureRemoteSessionAsync(It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(StopReason.Refusal), false));

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        viewModel.ReplaceChatService(CreateConnectedChatService().Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        viewModel.CurrentPrompt = "refuse me";
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();
        Assert.True(viewModel.CanSendPromptUi);

        await viewModel.SendPromptCommand.ExecuteAsync(null);
        syncContext.RunAll();

        var state = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.Failed, state.ActiveTurn!.Phase);
        Assert.Contains("refusal", state.ActiveTurn.FailureMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_AgentThoughtUpdate_AdvancesStoreBackedTurnToThinking()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(chatService.Object);

        var initialState = (await fixture.GetStateAsync()) with 
        { 
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent, DateTime.UtcNow, DateTime.UtcNow)
        };
        await fixture.UpdateStateAsync(_ => initialState);
        syncContext.RunAll();

        var update = new AgentThoughtUpdate { Content = new TextContentBlock("thinking...") };
        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", update));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return (await fixture.GetStateAsync()).ActiveTurn?.Phase == ChatTurnPhase.Thinking;
        });

        var state = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.Thinking, state.ActiveTurn!.Phase);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_ToolCallUpdate_AdvancesStoreBackedTurnToToolPending()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(chatService.Object);

        var initialState = (await fixture.GetStateAsync()) with 
        { 
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent, DateTime.UtcNow, DateTime.UtcNow)
        };
        await fixture.UpdateStateAsync(_ => initialState);
        syncContext.RunAll();

        var update = new ToolCallUpdate(toolCallId: "call-1", kind: ToolCallKind.Read, title: "title");
        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", update));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var state = await fixture.GetStateAsync();
            return state.ActiveTurn?.Phase == ChatTurnPhase.ToolPending
                && state.ActiveTurn.ToolCallId == "call-1";
        });

        var state = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.ToolPending, state.ActiveTurn!.Phase);
        Assert.Equal("call-1", state.ActiveTurn.ToolCallId);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_AgentMessageUpdate_AdvancesStoreBackedTurnToResponding()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(chatService.Object);

        var initialState = (await fixture.GetStateAsync()) with 
        { 
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent, DateTime.UtcNow, DateTime.UtcNow)
        };
        await fixture.UpdateStateAsync(_ => initialState);
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new AgentMessageUpdate(new TextContentBlock("hello"))));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return (await fixture.GetStateAsync()).ActiveTurn?.Phase == ChatTurnPhase.Responding;
        });

        var state = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.Responding, state.ActiveTurn!.Phase);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_ToolCallStatusCancelled_AdvancesStoreBackedTurnToCancelled()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(chatService.Object);

        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            Transcript = ImmutableList.Create(new ConversationMessageSnapshot
            {
                Id = "tool-1",
                Timestamp = DateTime.UtcNow,
                ContentType = "tool_call",
                ToolCallId = "call-1",
                ToolCallStatus = ToolCallStatus.InProgress,
                Title = "title"
            }),
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.ToolRunning, DateTime.UtcNow, DateTime.UtcNow)
        };
        await fixture.UpdateStateAsync(_ => initialState);
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new ToolCallStatusUpdate
            {
                ToolCallId = "call-1",
                Status = ToolCallStatus.Cancelled
            }));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var state = await fixture.GetStateAsync();
            var transcript = state.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            return state.ActiveTurn?.Phase == ChatTurnPhase.Cancelled
                && transcript.Single(message => string.Equals(message.Id, "tool-1", StringComparison.Ordinal)).ToolCallStatus == ToolCallStatus.Cancelled;
        });

        var state = await fixture.GetStateAsync();
        var transcript = state.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.Equal(ChatTurnPhase.Cancelled, state.ActiveTurn!.Phase);
        Assert.Equal(ToolCallStatus.Cancelled, transcript.Single(message => string.Equals(message.Id, "tool-1", StringComparison.Ordinal)).ToolCallStatus);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_ToolCallStatusUpdate_CreatesTranscriptEntryFromIncrementalSchemaFields()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(chatService.Object);

        var inputJson = JsonDocument.Parse("""{ "targetMode": "plan" }""").RootElement.Clone();
        var outputJson = JsonDocument.Parse("""{ "applied": true }""").RootElement.Clone();

        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.ToolPending, DateTime.UtcNow, DateTime.UtcNow)
        };
        await fixture.UpdateStateAsync(_ => initialState);
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new ToolCallStatusUpdate
            {
                ToolCallId = "call-1",
                Title = "Switch mode",
                Kind = ToolCallKind.SwitchMode,
                Status = ToolCallStatus.Completed,
                RawInput = inputJson,
                RawOutput = outputJson
            }));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var state = await fixture.GetStateAsync();
            var transcript = state.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            return transcript.Any(message =>
                string.Equals(message.ToolCallId, "call-1", StringComparison.Ordinal)
                && string.Equals(message.Title, "Switch mode", StringComparison.Ordinal)
                && message.ToolCallKind == ToolCallKind.SwitchMode
                && message.ToolCallStatus == ToolCallStatus.Completed);
        });

        var finalState = await fixture.GetStateAsync();
        var toolCallMessage = Assert.Single((finalState.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty)
            .Where(message => string.Equals(message.ToolCallId, "call-1", StringComparison.Ordinal)));
        Assert.Equal("Switch mode", toolCallMessage.Title);
        Assert.Equal(ToolCallKind.SwitchMode, toolCallMessage.ToolCallKind);
        Assert.Equal(ToolCallStatus.Completed, toolCallMessage.ToolCallStatus);
        Assert.Equal("plan", JsonDocument.Parse(toolCallMessage.ToolCallJson!).RootElement.GetProperty("targetMode").GetString());
        Assert.True(JsonDocument.Parse(toolCallMessage.TextContent).RootElement.GetProperty("applied").GetBoolean());
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_SessionInfoUpdate_RefreshesDisplayNameAndCatalogMetadata()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((sessionId, cwd) =>
            {
                var session = new Session(sessionId, cwd);
                sessions[sessionId] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(sessionId => sessions.TryGetValue(sessionId, out var session) ? session : null);
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((sessionId, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(sessionId, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(sessionId => sessions.Remove(sessionId));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\demo");
        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-1" });
        await WaitForConditionAsync(() => Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)));

        var conversationId = fixture.ViewModel.CurrentSessionId;
        Assert.NotNull(conversationId);
        var updatedAt = "2026-03-24T03:00:00Z";

        await InvokePrivateTaskAsync(
            fixture.ViewModel,
            "ApplySessionInfoUpdateAsync",
            "conv-1",
            new SessionInfoUpdate
            {
                Title = "Renamed by agent",
                UpdatedAt = updatedAt
            });

        await WaitForConditionAsync(() =>
        {
            var item = fixture.Workspace.GetCatalog().Single(entry => string.Equals(entry.ConversationId, conversationId, StringComparison.Ordinal));
            return Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionDisplayName, "Renamed by agent", StringComparison.Ordinal)
                && string.Equals(item.DisplayName, "Renamed by agent", StringComparison.Ordinal));
        });

        var expectedUpdatedAt = DateTimeOffset.Parse(updatedAt).UtcDateTime;
        var catalogItem = fixture.Workspace.GetCatalog().Single(entry => string.Equals(entry.ConversationId, conversationId, StringComparison.Ordinal));
        Assert.Equal("Renamed by agent", fixture.ViewModel.CurrentSessionDisplayName);
        Assert.Equal("Renamed by agent", catalogItem.DisplayName);
        Assert.Equal(expectedUpdatedAt, catalogItem.LastUpdatedAt);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_SessionInfoUpdate_ForBackgroundConversation_RefreshesCatalogMetadata()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((sessionId, cwd) =>
            {
                var session = new Session(sessionId, cwd);
                sessions[sessionId] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(sessionId => sessions.TryGetValue(sessionId, out var session) ? session : null);
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((sessionId, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(sessionId, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(sessionId => sessions.Remove(sessionId));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-2",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc)));

        var chatService = CreateConnectedChatService();
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-a"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-a"))
        });
        await WaitForConditionAsync(() => Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)));

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-2",
                new SessionInfoUpdate
                {
                    Title = "Background renamed",
                    UpdatedAt = "2026-03-24T03:00:00Z"
                }));

        await WaitForConditionAsync(() =>
        {
            var item = fixture.Workspace.GetCatalog().Single(entry => string.Equals(entry.ConversationId, "conv-2", StringComparison.Ordinal));
            return Task.FromResult(string.Equals(item.DisplayName, "Background renamed", StringComparison.Ordinal));
        });

        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);
        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_AgentMessageUpdate_AppendsTranscriptUsingStoreConversationEvenIfUiSessionIdMutatesMidUpdate()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(chatService.Object);

        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent, DateTime.UtcNow, DateTime.UtcNow)
        };
        await fixture.UpdateStateAsync(_ => initialState);
        syncContext.RunAll();

        fixture.ChatStore.AfterDispatch = action =>
        {
            if (action is AdvanceTurnPhaseAction { NewPhase: ChatTurnPhase.Responding })
            {
                SetPrivateField(viewModel, "_currentSessionId", "conv-2");
            }
        };

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new AgentMessageUpdate(new TextContentBlock("hello"))));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var state = await fixture.GetStateAsync();
            var transcript = state.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            return state.ActiveTurn?.Phase == ChatTurnPhase.Responding
                && transcript.Any(message => !message.IsOutgoing && string.Equals(message.TextContent, "hello", StringComparison.Ordinal));
        });

        var finalState = await fixture.GetStateAsync();
        var finalTranscript = finalState.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.Contains(finalTranscript, message =>
            !message.IsOutgoing && string.Equals(message.TextContent, "hello", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_UsageUpdate_IsSafeNoOpWithoutUnhandledLog()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(chatService.Object);

        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent, DateTime.UtcNow, DateTime.UtcNow),
            Generation = 42
        };
        await fixture.UpdateStateAsync(_ => initialState);
        syncContext.RunAll();

        var logInvocationCountBefore = fixture.ViewModelLogger.Invocations.Count;

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new UsageUpdate()));

        await Task.Delay(50);
        syncContext.RunAll();

        var state = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.WaitingForAgent, state.ActiveTurn!.Phase);
        Assert.Equal(42, state.Generation);

        var newLogInvocations = fixture.ViewModelLogger.Invocations.Skip(logInvocationCountBefore);
        Assert.DoesNotContain(newLogInvocations, invocation =>
            string.Equals(invocation.Method.Name, "Log", StringComparison.Ordinal)
            && invocation.Arguments.Count >= 3
            && invocation.Arguments[0] is LogLevel level
            && level == LogLevel.Information
            && string.Equals(
                invocation.Arguments[2]?.ToString(),
                "Unhandled session update type: UsageUpdate",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_LoadSessionIncludesEmptyMcpServersArray()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\demo");
        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);

        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        SessionLoadParams? capturedParams = null;
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Callback<SessionLoadParams, CancellationToken>((value, _) => capturedParams = value)
            .ReturnsAsync(SessionLoadResponse.Completed);
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });

        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)));

        var hydrated = await fixture.ViewModel.HydrateActiveConversationAsync();

        Assert.True(hydrated);
        Assert.NotNull(capturedParams);
        Assert.Equal("remote-1", capturedParams!.SessionId);
        Assert.Equal(@"C:\repo\demo", capturedParams.Cwd);
        Assert.NotNull(capturedParams.McpServers);
        Assert.Empty(capturedParams.McpServers!);
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenRemoteMetadataProvidesNewCwd_UsesRemoteCwdAsSsot()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\stale");
        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);

        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(
            loadSession: true,
            sessionCapabilities: new SessionCapabilities
            {
                List = new SessionListCapabilities()
            }));
        chatService.Setup(service => service.ListSessionsAsync(It.IsAny<SessionListParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionListResponse
            {
                Sessions =
                [
                    new AgentSessionInfo
                    {
                        SessionId = "remote-1",
                        Cwd = @"C:\repo\fresh",
                        Title = "Remote title",
                        UpdatedAt = "2026-03-28T12:34:56Z"
                    }
                ]
            });
        SessionLoadParams? capturedParams = null;
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Callback<SessionLoadParams, CancellationToken>((value, _) => capturedParams = value)
            .ReturnsAsync(SessionLoadResponse.Completed);
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });

        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)));

        var hydrated = await fixture.ViewModel.HydrateActiveConversationAsync();

        Assert.True(hydrated);
        Assert.NotNull(capturedParams);
        Assert.Equal(@"C:\repo\fresh", capturedParams!.Cwd);
        Assert.Equal(@"C:\repo\fresh", sessions["conv-1"].Cwd);
    }

    [Fact]
    public async Task ActivateConversationAsync_RemoteBoundConversation_LoadsRemoteSessionWhenConnected()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        var loadedRemoteSessionId = (string?)null;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.SetupGet(service => service.CurrentSessionId).Returns(() => loadedRemoteSessionId);
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Callback<SessionLoadParams, CancellationToken>((parameters, _) => loadedRemoteSessionId = parameters.SessionId)
            .ReturnsAsync(SessionLoadResponse.Completed);

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, CancellationToken>(async (profile, _, sink, _) =>
            {
                sink.SelectProfile(profile);
                sink.ReplaceChatService(chatService.Object);
                await fixture!.DispatchConnectionAsync(new SetSelectedProfileAction(profile.Id));
                await fixture!.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });

        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio });
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-1",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-2",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpdateRemoteBinding("conv-1", null, "profile-1");
            fixture.Workspace.UpdateRemoteBinding("conv-2", "remote-2", "profile-1");

            fixture.ViewModel.ReplaceChatService(chatService.Object);

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", null, "profile-1"))
                    .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
            });
            await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
            await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

            Assert.Equal("conv-1", (await fixture.GetStateAsync()).HydratedConversationId);

            var activated = await InvokePrivateTaskAsync<bool>(fixture.ViewModel, "ActivateConversationAsync", "conv-2", CancellationToken.None);

            Assert.True(activated, fixture.ViewModel.ErrorMessage);

        chatService.Verify(
                service => service.LoadSessionAsync(
                    It.Is<SessionLoadParams>(parameters =>
                        string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)
                        && string.Equals(parameters.Cwd, @"C:\repo\two", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhileRemoteLoadIsRunning_KeepsOverlayVisible()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, _) =>
            {
                loadStarted.TrySetResult(null);
                await allowLoadCompletion.Task;
                return SessionLoadResponse.Completed;
            });
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

        Assert.Equal("conv-1", (await fixture.GetStateAsync()).HydratedConversationId);

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(fixture.ViewModel.IsOverlayVisible);
        Assert.True(
            IsUserFriendlyHydrationOverlayStatus(fixture.ViewModel.OverlayStatusText),
            $"Unexpected hydration overlay status: {fixture.ViewModel.OverlayStatusText}");

        allowLoadCompletion.TrySetResult(null);
        var hydrated = await hydrationTask;
        Assert.True(hydrated);
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_DoesNotHideOverlayBeforeBufferedReplayProjectsToUi()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = (_, _) =>
            {
                innerChatService!.RaiseSessionUpdate(new SessionUpdateEventArgs(
                    "remote-1",
                    new AgentMessageUpdate(new TextContentBlock("restored history"))));
                return Task.FromResult(SessionLoadResponse.Completed);
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)
                && string.Equals(fixture.ViewModel.CurrentRemoteSessionId, "remote-1", StringComparison.Ordinal));
        });

        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);
        Assert.Equal("remote-1", fixture.ViewModel.CurrentRemoteSessionId);

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();
        await syncContext.RunUntilCompletedAsync(hydrationTask);
        await hydrationTask;

        var sawOverlayVisible = false;
        var observedPrematureOverlayDismissal = false;

        while (syncContext.RunNext())
        {
            if (fixture.ViewModel.IsOverlayVisible)
            {
                sawOverlayVisible = true;
                continue;
            }

            if (sawOverlayVisible && fixture.ViewModel.MessageHistory.Count == 0)
            {
                observedPrematureOverlayDismissal = true;
                break;
            }
        }

        Assert.False(
            observedPrematureOverlayDismissal,
            "Loading overlay should stay visible until buffered ACP replay updates have projected into the active transcript.");
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenSessionLoadReturnsBeforeReplayStarts_KeepsOverlayVisibleUntilLateReplayProjects()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        var loadReturned = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = (_, _) =>
            {
                loadReturned.TrySetResult(null);
                return Task.FromResult(SessionLoadResponse.Completed);
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();

        while (!loadReturned.Task.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        syncContext.RunAll();
        await Task.Delay(250);
        syncContext.RunAll();

        Assert.False(
            hydrationTask.IsCompleted,
            "Remote hydration should stay pending after session/load returns when replay has not started yet.");
        Assert.True(fixture.ViewModel.IsOverlayVisible);
        Assert.True(
            IsUserFriendlyHydrationOverlayStatus(fixture.ViewModel.OverlayStatusText),
            $"Unexpected hydration overlay status: {fixture.ViewModel.OverlayStatusText}");

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("late replay"))));

        await syncContext.RunUntilCompletedAsync(hydrationTask);

        Assert.True(await hydrationTask);
        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return fixture.ViewModel.MessageHistory.Any(message =>
                string.Equals(message.TextContent, "late replay", StringComparison.Ordinal));
        });
        await WaitForConditionAsync(() => Task.FromResult(!fixture.ViewModel.IsOverlayVisible));
    }

    [Fact]
    public async Task Overlay_WhenActiveConversationIsHydratingFromStoreProjection_RemainsVisible()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            IsHydrating = true
        });
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        Assert.True(fixture.ViewModel.IsOverlayVisible);
        Assert.True(
            IsUserFriendlyHydrationOverlayStatus(fixture.ViewModel.OverlayStatusText),
            $"Unexpected hydration overlay status: {fixture.ViewModel.OverlayStatusText}");
    }

    [Fact]
    public async Task Overlay_WhenHydratingAndTranscriptHasProjectedMessages_ShowsLoadedMessageCountInStatus()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            IsHydrating = true,
            Transcript = ImmutableList.Create(
                new ConversationMessageSnapshot
                {
                    Id = "msg-1",
                    Timestamp = new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc),
                    ContentType = "text",
                    TextContent = "first replay chunk"
                },
                new ConversationMessageSnapshot
                {
                    Id = "msg-2",
                    Timestamp = new DateTime(2026, 3, 30, 0, 0, 1, DateTimeKind.Utc),
                    ContentType = "text",
                    TextContent = "second replay chunk"
                })
        });
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        Assert.True(fixture.ViewModel.IsOverlayVisible);
        Assert.Contains("已加载 2 条消息", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenReplayStartsWithPlanUpdate_KeepsOverlayVisibleUntilTranscriptProjects()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        var loadReturned = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = (_, _) =>
            {
                loadReturned.TrySetResult(null);
                return Task.FromResult(SessionLoadResponse.Completed);
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();

        while (!loadReturned.Task.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        syncContext.RunAll();
        await Task.Delay(250);
        syncContext.RunAll();

        Assert.False(hydrationTask.IsCompleted);
        Assert.True(fixture.ViewModel.IsOverlayVisible);

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new PlanUpdate(title: "restored plan")));

        syncContext.RunAll();
        await Task.Delay(250);
        syncContext.RunAll();

        Assert.False(
            hydrationTask.IsCompleted,
            "Non-transcript replay state should not complete hydration before transcript history is replayed.");
        Assert.True(
            fixture.ViewModel.IsOverlayVisible,
            "Non-transcript replay updates must not dismiss the history loading overlay.");

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("late replay after plan"))));

        await syncContext.RunUntilCompletedAsync(hydrationTask);

        Assert.True(await hydrationTask);
        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return fixture.ViewModel.MessageHistory.Any(message =>
                string.Equals(message.TextContent, "late replay after plan", StringComparison.Ordinal));
        });
        await WaitForConditionAsync(() => Task.FromResult(!fixture.ViewModel.IsOverlayVisible));
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenReplayStartsWithSessionInfoUpdate_KeepsOverlayVisibleUntilTranscriptProjects()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        var loadReturned = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = (_, _) =>
            {
                loadReturned.TrySetResult(null);
                return Task.FromResult(SessionLoadResponse.Completed);
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();

        while (!loadReturned.Task.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        syncContext.RunAll();
        await Task.Delay(250);
        syncContext.RunAll();

        Assert.False(hydrationTask.IsCompleted);
        Assert.True(fixture.ViewModel.IsOverlayVisible);

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new SessionInfoUpdate
            {
                Title = "remote title only"
            }));

        syncContext.RunAll();
        await Task.Delay(300);
        syncContext.RunAll();

        Assert.False(
            hydrationTask.IsCompleted,
            "Session metadata replay must not complete hydration before transcript history is replayed.");
        Assert.True(
            fixture.ViewModel.IsOverlayVisible,
            "Session metadata replay must not dismiss the history loading overlay.");

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("late replay after title"))));

        await syncContext.RunUntilCompletedAsync(hydrationTask);

        Assert.True(await hydrationTask);
        Assert.Equal("remote title only", fixture.ViewModel.CurrentSessionDisplayName);
        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return fixture.ViewModel.MessageHistory.Any(message =>
                string.Equals(message.TextContent, "late replay after title", StringComparison.Ordinal));
        });
        await WaitForConditionAsync(() => Task.FromResult(!fixture.ViewModel.IsOverlayVisible));
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenReplayArrivesInBursts_KeepsOverlayVisibleUntilReplaySettles()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        var loadReturned = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = (_, _) =>
            {
                loadReturned.TrySetResult(null);
                return Task.FromResult(SessionLoadResponse.Completed);
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();

        while (!loadReturned.Task.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        syncContext.RunAll();
        await Task.Delay(100);
        syncContext.RunAll();

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("first replay burst"))));

        var overlayDismissedBeforeSecondBurst = false;
        var guardDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(1500);
        while (DateTime.UtcNow < guardDeadline)
        {
            syncContext.RunAll();
            if (!fixture.ViewModel.IsOverlayVisible)
            {
                overlayDismissedBeforeSecondBurst = true;
                break;
            }

            await Task.Delay(25);
        }

        Assert.False(
            overlayDismissedBeforeSecondBurst,
            "Loading overlay must not disappear after the first replay burst while later replay data can still arrive.");
        Assert.False(
            hydrationTask.IsCompleted,
            "Hydration should stay pending while remote replay is still arriving in multiple bursts.");
        Assert.True(
            fixture.ViewModel.IsOverlayVisible,
            "Loading overlay must remain visible until the remote replay burst stabilizes.");

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("second replay burst"))));

        await syncContext.RunUntilCompletedAsync(hydrationTask);
        await Task.Delay(100);
        syncContext.RunAll();

        Assert.True(await hydrationTask);
        Assert.Contains(
            fixture.ViewModel.MessageHistory,
            message => (message.TextContent?.Contains("first replay burst", StringComparison.Ordinal) ?? false)
                && (message.TextContent?.Contains("second replay burst", StringComparison.Ordinal) ?? false));
        await WaitForConditionAsync(() => Task.FromResult(!fixture.ViewModel.IsOverlayVisible), timeoutMilliseconds: 4000);
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenFirstReplayOnlyRestoresKnownPrompt_KeepsOverlayVisibleUntilAdditionalHistoryArrives()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        var loadReturned = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = (_, _) =>
            {
                loadReturned.TrySetResult(null);
                return Task.FromResult(SessionLoadResponse.Completed);
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            Transcript = ImmutableList.Create(new ConversationMessageSnapshot
            {
                Id = "seed-1",
                IsOutgoing = true,
                ContentType = "text",
                TextContent = "known local prompt",
                Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            })
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();

        while (!loadReturned.Task.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        syncContext.RunAll();
        await Task.Delay(100);
        syncContext.RunAll();

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new UserMessageUpdate(new TextContentBlock("known local prompt"))));

        var overlayDismissedBeforeAdditionalHistory = false;
        var guardDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(3200);
        while (DateTime.UtcNow < guardDeadline)
        {
            syncContext.RunAll();
            if (!fixture.ViewModel.IsOverlayVisible)
            {
                overlayDismissedBeforeAdditionalHistory = true;
                break;
            }

            await Task.Delay(25);
        }

        Assert.False(
            overlayDismissedBeforeAdditionalHistory,
            "Replay should not dismiss loading when it has only restored transcript content that was already known locally.");
        Assert.False(hydrationTask.IsCompleted);

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("remote history answer"))));

        await syncContext.RunUntilCompletedAsync(hydrationTask);
        await Task.Delay(100);
        syncContext.RunAll();

        Assert.True(await hydrationTask);
        Assert.Contains(
            fixture.ViewModel.MessageHistory,
            message => (message.TextContent?.Contains("remote history answer", StringComparison.Ordinal) ?? false));
        await WaitForConditionAsync(() => Task.FromResult(!fixture.ViewModel.IsOverlayVisible), timeoutMilliseconds: 6000);
    }

    [Fact]
    public async Task ActivateConversationAsync_WhileActivationStillRunning_ShowsOverlayBeforeHydrationStarts()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var activationStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowActivationCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync("conv-1", It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (_, _) =>
            {
                activationStarted.TrySetResult(null);
                await allowActivationCompletion.Task;
                return new ConversationActivationResult(true, "conv-1", null);
            });

        await using var fixture = CreateViewModel(syncContext, conversationActivationCoordinator: activationCoordinator.Object);

        var activationTask = InvokePrivateTaskAsync(fixture.ViewModel, "ActivateConversationAsync", "conv-1", CancellationToken.None);
        await activationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(fixture.ViewModel.IsOverlayVisible);

        allowActivationCompletion.TrySetResult(null);
        await activationTask;
    }

    [Fact]
    public async Task ConversationActivationPreview_WhenCalledOnCapturedUiContext_AppliesSynchronously()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new QueueingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            await using var fixture = CreateViewModel(syncContext);
            var preview = (IConversationActivationPreview)fixture.ViewModel;
            var pendingBeforePrime = syncContext.PendingCount;

            preview.PrimeSessionSwitchPreview("conv-1");

            Assert.Equal(pendingBeforePrime, syncContext.PendingCount);
            Assert.True(fixture.ViewModel.IsOverlayVisible);
            Assert.True(fixture.ViewModel.ShouldShowBlockingLoadingMask);
            Assert.Contains("切换", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);

            var pendingBeforeClear = syncContext.PendingCount;
            preview.ClearSessionSwitchPreview("conv-1");

            Assert.Equal(pendingBeforeClear, syncContext.PendingCount);
            Assert.False(fixture.ViewModel.IsOverlayVisible);
            Assert.False(fixture.ViewModel.ShouldShowBlockingLoadingMask);
            Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task SelectAndHydrateConversationAsync_WhenActivationFails_ClearsLayoutLoadingState()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationActivationResult(false, "conv-1", "ActivationFailed"));

        await using var fixture = CreateViewModel(syncContext, conversationActivationCoordinator: activationCoordinator.Object);

        await InvokePrivateTaskAsync(fixture.ViewModel, "SelectAndHydrateConversationAsync", "conv-1");

        Assert.False(fixture.ViewModel.IsLayoutLoading);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
        Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
    }

    [Fact]
    public async Task SelectAndHydrateConversationAsync_WhenActivatedConversationHasNoMessages_ClearsLayoutLoadingState()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationActivationResult(true, "conv-1", null));

        await using var fixture = CreateViewModel(syncContext, conversationActivationCoordinator: activationCoordinator.Object);
        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-1" });

        await InvokePrivateTaskAsync(fixture.ViewModel, "SelectAndHydrateConversationAsync", "conv-1");

        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);
        Assert.Empty(fixture.ViewModel.MessageHistory);
        Assert.False(fixture.ViewModel.IsLayoutLoading);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
        Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
    }

    [Fact]
    public async Task SwitchConversationAsync_RemoteBoundConversation_WhenProfileConnectIsPending_KeepsOverlayVisible()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        var allowConnectCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, CancellationToken>(async (_, _, _, cancellationToken) =>
            {
                await allowConnectCompletion.Task.WaitAsync(cancellationToken);
                return new AcpTransportApplyResult(Mock.Of<IChatService>(), new InitializeResponse());
            });

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio });
        var restoreTask = fixture.ViewModel.RestoreAsync();
        await syncContext.RunUntilCompletedAsync(restoreTask);

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-2",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        syncContext.RunAll();

        var switchTask = fixture.ViewModel.SwitchConversationAsync("conv-2");

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.ViewModel.IsOverlayVisible || switchTask.IsCompleted);
        });

        Assert.False(switchTask.IsCompleted);
        Assert.True(fixture.ViewModel.IsOverlayVisible);

        allowConnectCompletion.TrySetResult(null);
        await syncContext.RunUntilCompletedAsync(switchTask);
    }

    [Fact]
    public async Task Overlay_WhenCurrentConversationIsLocal_IgnoresGlobalConnectionLifecycle()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connecting));

        Assert.Equal("conv-local", fixture.ViewModel.CurrentSessionId);
        Assert.Null(fixture.ViewModel.CurrentRemoteSessionId);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
        Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
    }

    [Fact]
    public async Task Overlay_WhenDifferentProfileIsConnectingForCurrentRemoteConversation_RemainsHidden()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-remote",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-2"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connecting));

        Assert.Equal("conv-remote", fixture.ViewModel.CurrentSessionId);
        Assert.Equal("remote-1", fixture.ViewModel.CurrentRemoteSessionId);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
        Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
    }

    [Fact]
    public async Task Overlay_HistoryOwnerFromDifferentConversation_DoesNotLeakStatusToCurrentSession()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-2" });
        SetPrivateField(fixture.ViewModel, "_historyOverlayConversationId", "conv-1");

        Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
        Assert.False(fixture.ViewModel.ShouldShowBlockingLoadingMask);
        Assert.False(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);
        Assert.False(fixture.ViewModel.ShouldShowLoadingOverlayPresenter);
        Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
    }

    [Fact]
    public async Task Overlay_ConnectionLifecycleOwnerFromDifferentConversation_DoesNotLeakStatusToCurrentSession()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-2" });
        SetPrivateField(fixture.ViewModel, "_connectionLifecycleOverlayConversationId", "conv-1");
        fixture.ViewModel.IsConnecting = true;

        Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
        Assert.False(fixture.ViewModel.ShouldShowBlockingLoadingMask);
        Assert.False(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);
        Assert.False(fixture.ViewModel.ShouldShowLoadingOverlayPresenter);
        Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
    }

    [Fact]
    public async Task Overlay_WhenCurrentSessionChanges_RaisesDerivedPropertyNotifications()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-1" });
        SetPrivateField(fixture.ViewModel, "_historyOverlayConversationId", "conv-1");

        var raised = new List<string>();
        fixture.ViewModel.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                raised.Add(e.PropertyName!);
            }
        };

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-2" });

        Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
        Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
        Assert.Contains(nameof(ChatViewModel.IsOverlayVisible), raised);
        Assert.Contains(nameof(ChatViewModel.OverlayStatusText), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldShowBlockingLoadingMask), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldShowLoadingOverlayStatusPill), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldShowLoadingOverlayPresenter), raised);
    }

    [Fact]
    public async Task Overlay_HydratingHistoryStatus_ReportsLoadedMessageCount()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            IsHydrating = true,
            Transcript = ImmutableList<ConversationMessageSnapshot>.Empty
        });

        Assert.True(
            IsUserFriendlyHydrationOverlayStatus(fixture.ViewModel.OverlayStatusText),
            $"Unexpected hydration overlay status: {fixture.ViewModel.OverlayStatusText}");

        await fixture.UpdateStateAsync(state => state with
        {
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "hydrated-1",
                    Timestamp = new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hydrated message"
                }
            ]
        });

        Assert.Contains("已加载 1 条消息", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overlay_ConnectionStatus_UsesUserFriendlyLanguageWithoutProtocolJargon()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-1" });
        SetPrivateField(fixture.ViewModel, "_connectionLifecycleOverlayConversationId", "conv-1");

        fixture.ViewModel.IsConnecting = true;
        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.OverlayStatusText));
        Assert.Contains("连接", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("ACP", fixture.ViewModel.OverlayStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("协议", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);

        fixture.ViewModel.IsConnecting = false;
        fixture.ViewModel.IsInitializing = true;
        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.OverlayStatusText));
        Assert.Contains("准备", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("ACP", fixture.ViewModel.OverlayStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("协议", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overlay_StatusPill_WhenVisible_AlwaysShowsUserFriendlyActionableText()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            IsHydrating = true
        });

        Assert.True(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);
        Assert.True(IsUserFriendlyHydrationOverlayStatus(fixture.ViewModel.OverlayStatusText));
        Assert.DoesNotContain("ACP", fixture.ViewModel.OverlayStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("协议", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Suite", "Smoke")]
    public async Task ConversationSessionSwitcherContract_RemoteBoundConversation_CompletesAfterLocalActivationWhileRemoteHydrationContinues()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-2",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, _) =>
            {
                loadStarted.TrySetResult(null);
                await allowLoadCompletion.Task;
                return SessionLoadResponse.Completed;
            });
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

        var switcher = (IConversationSessionSwitcher)fixture.ViewModel;
        var activationTask = switcher.SwitchConversationAsync("conv-2");

        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(activationTask.IsCompletedSuccessfully);
        Assert.True(await activationTask);
        Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
        Assert.True(fixture.ViewModel.IsOverlayVisible);
        Assert.True(
            IsUserFriendlyHydrationOverlayStatus(fixture.ViewModel.OverlayStatusText),
            $"Unexpected hydration overlay status: {fixture.ViewModel.OverlayStatusText}");

        allowLoadCompletion.TrySetResult(null);
        await WaitForConditionAsync(() => Task.FromResult(!fixture.ViewModel.IsOverlayVisible), timeoutMilliseconds: 7000);
    }

    [Fact]
    public async Task ConversationSessionSwitcherContract_RemoteBoundConversation_DoesNotFlashWorkspaceCachedTranscriptBeforeRemoteReplay()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await syncContext.RunUntilCompletedAsync(fixture.ViewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-2",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "cached-1",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "cached first message"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, _) =>
            {
                loadStarted.TrySetResult(null);
                await allowLoadCompletion.Task;
                return SessionLoadResponse.Completed;
            });
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var switcher = (IConversationSessionSwitcher)fixture.ViewModel;
        var activationTask = switcher.SwitchConversationAsync("conv-2");

        while (!activationTask.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        Assert.True(activationTask.IsCompletedSuccessfully);
        Assert.True(await activationTask);
        Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
        Assert.True(
            fixture.ViewModel.IsOverlayVisible || fixture.ViewModel.MessageHistory.Count == 0,
            "The remote switch should not surface cached transcript before remote hydration UI takes over.");
        Assert.Empty(fixture.ViewModel.MessageHistory);
        Assert.DoesNotContain(
            fixture.ViewModel.MessageHistory,
            message => string.Equals(message.TextContent, "cached first message", StringComparison.Ordinal));

        allowLoadCompletion.TrySetResult(null);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(loadStarted.Task.IsCompleted);
        }, timeoutMilliseconds: 8000);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(!fixture.ViewModel.IsOverlayVisible);
        }, timeoutMilliseconds: 7000);
    }

    [Fact]
    [Trait("Suite", "Smoke")]
    public async Task ConversationSessionSwitcherContract_RemoteBoundConversation_WhenReplayStartsAfterLoadResponse_KeepsOverlayVisible()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        var restoreTask = fixture.ViewModel.RestoreAsync();
        await syncContext.RunUntilCompletedAsync(restoreTask);

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-2",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var loadReturned = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = (_, _) =>
            {
                loadReturned.TrySetResult(null);
                return Task.FromResult(SessionLoadResponse.Completed);
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var switcher = (IConversationSessionSwitcher)fixture.ViewModel;
        var activationTask = switcher.SwitchConversationAsync("conv-2");

        while (!activationTask.IsCompleted || !loadReturned.Task.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        syncContext.RunAll();
        await Task.Delay(250);
        syncContext.RunAll();

        Assert.True(activationTask.IsCompletedSuccessfully);
        Assert.True(await activationTask);
        Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
        Assert.True(
            fixture.ViewModel.IsOverlayVisible,
            "Loading overlay should remain visible after the local switch commits when remote replay has not projected yet.");
        Assert.True(
            IsUserFriendlyHydrationOverlayStatus(fixture.ViewModel.OverlayStatusText),
            $"Unexpected hydration overlay status: {fixture.ViewModel.OverlayStatusText}");

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-2",
            new AgentMessageUpdate(new TextContentBlock("late replay"))));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return fixture.ViewModel.MessageHistory.Any(message =>
                string.Equals(message.TextContent, "late replay", StringComparison.Ordinal));
        });

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return !fixture.ViewModel.IsOverlayVisible;
        }, timeoutMilliseconds: 5000);
    }

    [Fact]
    public async Task ConversationSessionSwitcherContract_ReclickingSamePendingRemoteConversation_DoesNotQueueDuplicateRemoteLoadBeforeLaterSwitch()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        var restoreTask = fixture.ViewModel.RestoreAsync();
        await syncContext.RunUntilCompletedAsync(restoreTask);

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "local-1",
                    Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "local"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-2",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc)));

        var firstRemoteLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstRemoteLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadOrder = new List<string>();
        var loadOrderSync = new object();
        var remoteOneLoadCalls = 0;

        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (parameters, _) =>
            {
                if (string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal))
                {
                    var callIndex = Interlocked.Increment(ref remoteOneLoadCalls);
                    lock (loadOrderSync)
                    {
                        loadOrder.Add($"remote-1#{callIndex}");
                    }

                    if (callIndex == 1)
                    {
                        firstRemoteLoadStarted.TrySetResult(null);
                        await allowFirstRemoteLoadCompletion.Task;
                    }

                    return SessionLoadResponse.Completed;
                }

                if (string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal))
                {
                    lock (loadOrderSync)
                    {
                        loadOrder.Add("remote-2#1");
                    }

                    return SessionLoadResponse.Completed;
                }

                throw new InvalidOperationException($"Unexpected session load: {parameters.SessionId}");
            });
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var switcher = (IConversationSessionSwitcher)fixture.ViewModel;

        var firstActivation = switcher.SwitchConversationAsync("conv-1");
        while (!firstActivation.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        Assert.True(await firstActivation);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(firstRemoteLoadStarted.Task.IsCompleted);
        }, timeoutMilliseconds: 4000);

        var duplicateActivation = switcher.SwitchConversationAsync("conv-1");
        while (!duplicateActivation.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        Assert.True(await duplicateActivation);

        var laterActivation = switcher.SwitchConversationAsync("conv-2");
        while (!laterActivation.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        Assert.True(await laterActivation);
        Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
        Assert.True(fixture.ViewModel.IsOverlayVisible);

        allowFirstRemoteLoadCompletion.TrySetResult(null);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            lock (loadOrderSync)
            {
                return Task.FromResult(loadOrder.Count >= 2);
            }
        }, timeoutMilliseconds: 4000);

        string[] observedOrder;
        lock (loadOrderSync)
        {
            observedOrder = loadOrder.ToArray();
        }

        Assert.Equal("remote-2#1", observedOrder[1]);
    }

    [Fact]
    public async Task ConversationSessionSwitcherContract_WhenEarlierRemoteLoadIsStillBlocked_LaterSelectionStartsLatestRemoteLoadWithoutWaiting()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        var firstLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (parameters, token) =>
            {
                if (string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal))
                {
                    firstLoadStarted.TrySetResult(null);
                    await allowFirstLoadCompletion.Task.WaitAsync(token);
                    return SessionLoadResponse.Completed;
                }

                if (string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal))
                {
                    secondLoadStarted.TrySetResult(null);
                    return SessionLoadResponse.Completed;
                }

                throw new InvalidOperationException($"Unexpected session load: {parameters.SessionId}");
            });
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var switcher = (IConversationSessionSwitcher)fixture.ViewModel;

        var firstActivation = switcher.SwitchConversationAsync("conv-1");
        while (!firstActivation.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        Assert.True(await firstActivation);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(firstLoadStarted.Task.IsCompleted);
        }, timeoutMilliseconds: 4000);

        var secondActivation = switcher.SwitchConversationAsync("conv-2");
        while (!secondActivation.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        Assert.True(await secondActivation);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(secondLoadStarted.Task.IsCompleted);
        }, timeoutMilliseconds: 1000);

        allowFirstLoadCompletion.TrySetResult(null);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(!fixture.ViewModel.IsOverlayVisible);
        }, timeoutMilliseconds: 4000);
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenEarlierRemoteActivationIsPending_LatestLocalSwitchWins()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        var allowConnectCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, CancellationToken>(async (profile, _, sink, cancellationToken) =>
            {
                await allowConnectCompletion.Task.WaitAsync(cancellationToken);
                sink.SelectProfile(profile);
                sink.ReplaceChatService(chatService.Object);
                await fixture!.DispatchConnectionAsync(new SetSelectedProfileAction(profile.Id));
                await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });

        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio });
            var restoreTask = fixture.ViewModel.RestoreAsync();
            await syncContext.RunUntilCompletedAsync(restoreTask);

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-local",
                Transcript:
                [
                    new ConversationMessageSnapshot
                    {
                        Id = "local-1",
                        Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "local"
                    }
                ],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-remote",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-local",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
            });
            syncContext.RunAll();

            var remoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote");

            await WaitForConditionAsync(() =>
            {
                syncContext.RunAll();
                return Task.FromResult(fixture.ViewModel.IsOverlayVisible || remoteSwitchTask.IsCompleted);
            });

            Assert.False(remoteSwitchTask.IsCompleted);

            var localSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-local");
            await syncContext.RunUntilCompletedAsync(localSwitchTask);

            Assert.True(await localSwitchTask);
            await WaitForConditionAsync(() =>
            {
                syncContext.RunAll();
                return Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-local", StringComparison.Ordinal));
            });

            allowConnectCompletion.TrySetResult(null);
            await syncContext.RunUntilCompletedAsync(remoteSwitchTask);

            Assert.False(await remoteSwitchTask);
            await WaitForConditionAsync(() =>
            {
                syncContext.RunAll();
                return Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-local", StringComparison.Ordinal));
            });

            var finalState = await fixture.GetStateAsync();
            Assert.Equal("conv-local", finalState.HydratedConversationId);
            Assert.Equal("local", Assert.Single(finalState.Transcript!).TextContent);
        }
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenSupersededByPureLocalConversation_HidesLoadingStatusPillImmediately()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        var allowConnectCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, CancellationToken>(async (profile, _, sink, cancellationToken) =>
            {
                await allowConnectCompletion.Task.WaitAsync(cancellationToken);
                sink.SelectProfile(profile);
                sink.ReplaceChatService(chatService.Object);
                await fixture!.DispatchConnectionAsync(new SetSelectedProfileAction(profile.Id));
                await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });

        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio });
            var restoreTask = fixture.ViewModel.RestoreAsync();
            await syncContext.RunUntilCompletedAsync(restoreTask);

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-local",
                Transcript:
                [
                    new ConversationMessageSnapshot
                    {
                        Id = "local-1",
                        Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "local"
                    }
                ],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-remote",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-local",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
            });
            syncContext.RunAll();

            var remoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote");

            await WaitForConditionAsync(() =>
            {
                syncContext.RunAll();
                return Task.FromResult(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill || remoteSwitchTask.IsCompleted);
            });

            Assert.False(remoteSwitchTask.IsCompleted);

            var localSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-local");
            await syncContext.RunUntilCompletedAsync(localSwitchTask);

            Assert.True(await localSwitchTask);
            await WaitForConditionAsync(() =>
            {
                syncContext.RunAll();
                return Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-local", StringComparison.Ordinal));
            });

            Assert.False(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);
            Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);

            allowConnectCompletion.TrySetResult(null);
            await syncContext.RunUntilCompletedAsync(remoteSwitchTask);
        }
    }

    [Fact]
    public async Task ConversationSessionSwitcherContract_WhenRemoteReplayArrivesBeforeLoadResponse_AfterLocalDetour_RemoteOverlayDismisses()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        var firstLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstLoadCanceled = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadInvocationCount = 0;

        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = async (parameters, cancellationToken) =>
            {
                var invocation = Interlocked.Increment(ref loadInvocationCount);
                if (invocation == 1)
                {
                    firstLoadStarted.TrySetResult(null);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        firstLoadCanceled.TrySetResult(null);
                        throw;
                    }
                }

                Assert.Equal(2, invocation);
                Assert.Equal("remote-1", parameters.SessionId);

                innerChatService!.RaiseSessionUpdate(new SessionUpdateEventArgs(
                    "remote-1",
                    new SessionInfoUpdate
                    {
                        Title = "Remote replay title",
                        UpdatedAt = "2026-03-30T00:00:00Z"
                    }));
                innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
                    "remote-1",
                    new AgentMessageUpdate(new TextContentBlock("remote replay chunk before load response"))));

                await Task.Yield();
                return SessionLoadResponse.Completed;
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        var restoreTask = fixture.ViewModel.RestoreAsync();
        await syncContext.RunUntilCompletedAsync(restoreTask);

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "local-1",
                    Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "local",
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var firstRemoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(firstLoadStarted.Task.IsCompleted);
        }, timeoutMilliseconds: 2000);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill || firstRemoteSwitchTask.IsCompleted);
        });

        Assert.False(firstRemoteSwitchTask.IsCompleted);

        var localSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-local");
        await syncContext.RunUntilCompletedAsync(localSwitchTask);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(firstLoadCanceled.Task.IsCompleted);
        }, timeoutMilliseconds: 2000);

        Assert.True(await localSwitchTask);
        Assert.Equal("conv-local", fixture.ViewModel.CurrentSessionId);
        Assert.False(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);

        var secondRemoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await syncContext.RunUntilCompletedAsync(secondRemoteSwitchTask);
        Assert.True(await secondRemoteSwitchTask);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            var replayProjected = fixture.ViewModel.MessageHistory.Any(message =>
                message.TextContent?.Contains("remote replay chunk before load response", StringComparison.Ordinal) == true);
            return Task.FromResult(replayProjected && !fixture.ViewModel.IsOverlayVisible);
        }, timeoutMilliseconds: 4000);

        Assert.Equal("conv-remote", fixture.ViewModel.CurrentSessionId);
        Assert.Contains(
            fixture.ViewModel.MessageHistory,
            message => message.TextContent?.Contains("remote replay chunk before load response", StringComparison.Ordinal) == true);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
    }

    [Fact]
    public async Task ConversationSessionSwitcherContract_WhenCanceledHydrationEmitsLateReplay_StaleReplayIsDiscardedBeforeNextRemoteActivation()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        var firstLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstLoadCanceled = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadInvocationCount = 0;

        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = async (parameters, cancellationToken) =>
            {
                var invocation = Interlocked.Increment(ref loadInvocationCount);
                if (invocation == 1)
                {
                    firstLoadStarted.TrySetResult(null);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        firstLoadCanceled.TrySetResult(null);
                        innerChatService!.RaiseSessionUpdate(new SessionUpdateEventArgs(
                            "remote-1",
                            new AgentMessageUpdate(new TextContentBlock("stale replay after cancel"))));
                        await Task.Yield();
                        throw;
                    }
                }

                Assert.Equal(2, invocation);
                Assert.Equal("remote-1", parameters.SessionId);
                innerChatService!.RaiseSessionUpdate(new SessionUpdateEventArgs(
                    "remote-1",
                    new AgentMessageUpdate(new TextContentBlock("fresh replay after restart"))));
                await Task.Yield();
                return SessionLoadResponse.Completed;
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        var restoreTask = fixture.ViewModel.RestoreAsync();
        await syncContext.RunUntilCompletedAsync(restoreTask);

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "local-1",
                    Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "local",
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var firstRemoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(firstLoadStarted.Task.IsCompleted);
        }, timeoutMilliseconds: 2000);

        var localSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-local");
        await syncContext.RunUntilCompletedAsync(localSwitchTask);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(firstLoadCanceled.Task.IsCompleted);
        }, timeoutMilliseconds: 2000);

        Assert.True(await localSwitchTask);
        await syncContext.RunUntilCompletedAsync(firstRemoteSwitchTask);
        Assert.False(await firstRemoteSwitchTask);
        syncContext.RunAll();
        Assert.DoesNotContain(
            fixture.ViewModel.MessageHistory,
            message => message.TextContent?.Contains("stale replay after cancel", StringComparison.Ordinal) == true);

        var secondRemoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await syncContext.RunUntilCompletedAsync(secondRemoteSwitchTask);
        Assert.True(await secondRemoteSwitchTask);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            var freshReplayProjected = fixture.ViewModel.MessageHistory.Any(message =>
                message.TextContent?.Contains("fresh replay after restart", StringComparison.Ordinal) == true);
            var staleReplayProjected = fixture.ViewModel.MessageHistory.Any(message =>
                message.TextContent?.Contains("stale replay after cancel", StringComparison.Ordinal) == true);
            return Task.FromResult(freshReplayProjected && !staleReplayProjected);
        }, timeoutMilliseconds: 8000);

        Assert.Equal("conv-remote", fixture.ViewModel.CurrentSessionId);
        Assert.Contains(
            fixture.ViewModel.MessageHistory,
            message => message.TextContent?.Contains("fresh replay after restart", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            fixture.ViewModel.MessageHistory,
            message => message.TextContent?.Contains("stale replay after cancel", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task ConversationSessionSwitcherContract_WhenSecondRemoteSelectionSupersedesFirst_OnlyLatestRemoteReplayCanAffectVisibleTranscript()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote-1", @"C:\repo\remote1");
        await sessionManager.Object.CreateSessionAsync("conv-remote-2", @"C:\repo\remote2");

        var remote1Started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote1Canceled = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = async (parameters, cancellationToken) =>
            {
                if (string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal))
                {
                    remote1Started.TrySetResult(null);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        remote1Canceled.TrySetResult(null);
                        innerChatService!.RaiseSessionUpdate(new SessionUpdateEventArgs(
                            "remote-1",
                            new AgentMessageUpdate(new TextContentBlock("stale remote-1 replay"))));
                        await Task.Yield();
                        throw;
                    }
                }

                Assert.Equal("remote-2", parameters.SessionId);
                innerChatService!.RaiseSessionUpdate(new SessionUpdateEventArgs(
                    "remote-2",
                    new AgentMessageUpdate(new TextContentBlock("fresh remote-2 replay"))));
                await Task.Yield();
                return SessionLoadResponse.Completed;
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await syncContext.RunUntilCompletedAsync(fixture.ViewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "local-1",
                    Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "local seed"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote-2",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc)));

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote-1", new ConversationBindingSlice("conv-remote-1", "remote-1", "profile-1"))
                .Add("conv-remote-2", new ConversationBindingSlice("conv-remote-2", "remote-2", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var firstRemoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote-1");
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(remote1Started.Task.IsCompleted);
        }, timeoutMilliseconds: 2500);

        var secondRemoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote-2");
        await syncContext.RunUntilCompletedAsync(secondRemoteSwitchTask);
        Assert.True(await secondRemoteSwitchTask);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(remote1Canceled.Task.IsCompleted);
        }, timeoutMilliseconds: 2500);
        await syncContext.RunUntilCompletedAsync(firstRemoteSwitchTask);
        Assert.False(await firstRemoteSwitchTask);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            var hasFreshRemote2 = fixture.ViewModel.MessageHistory.Any(message =>
                message.TextContent?.Contains("fresh remote-2 replay", StringComparison.Ordinal) == true);
            var hasStaleRemote1 = fixture.ViewModel.MessageHistory.Any(message =>
                message.TextContent?.Contains("stale remote-1 replay", StringComparison.Ordinal) == true);
            return Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-remote-2", StringComparison.Ordinal)
                && hasFreshRemote2
                && !hasStaleRemote1
                && !fixture.ViewModel.IsOverlayVisible);
        }, timeoutMilliseconds: 4500);

        Assert.Equal("conv-remote-2", fixture.ViewModel.CurrentSessionId);
        Assert.DoesNotContain(
            fixture.ViewModel.MessageHistory,
            message => message.TextContent?.Contains("stale remote-1 replay", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task SwitchConversationAsync_RemoteBoundConversation_WhenDisconnected_ConnectsProfileAndHydratesRemoteSession()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-2", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, _) =>
            {
                sink.SelectProfile(profile);
                sink.ReplaceChatService(chatService.Object);
                await fixture!.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });

        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio });
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-1",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-2",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                    .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
            });
            fixture.ViewModel.ReplaceChatService(null);

            var switched = await fixture.ViewModel.SwitchConversationAsync("conv-2");

            Assert.True(switched);
            commands.Verify(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-2", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()), Times.Once);
            chatService.Verify(
                service => service.LoadSessionAsync(
                    It.Is<SessionLoadParams>(parameters => string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task EnsureActiveConversationRemoteConnectionReadyAsync_WhenRemoteConversationIsDisconnected_ConnectsBoundProfile()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-2", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, _) =>
            {
                sink.SelectProfile(profile);
                sink.ReplaceChatService(chatService.Object);
                await fixture!.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });

        fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio });
            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-2",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
            });
            fixture.ViewModel.ReplaceChatService(null);

            var method = fixture.ViewModel.GetType().GetMethod(
                "EnsureActiveConversationRemoteConnectionReadyAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var readyTaskObject = method!.Invoke(fixture.ViewModel, ["conv-2", null, CancellationToken.None]);
            var readyTask = Assert.IsAssignableFrom<Task<bool>>(readyTaskObject);
            var ready = await readyTask;

            Assert.True(ready);
            commands.Verify(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-2", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task EnsureActiveConversationRemoteConnectionReadyAsync_WhenPendingConnectionFallsBackToDisconnectedWithoutError_StartsFreshConnect()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-2", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, _) =>
            {
                sink.SelectProfile(profile);
                sink.ReplaceChatService(chatService.Object);
                await fixture!.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });

        fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio });
            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-2",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
            });
            fixture.ViewModel.ReplaceChatService(null);
            await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
            await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Initializing));

            var method = fixture.ViewModel.GetType().GetMethod(
                "EnsureActiveConversationRemoteConnectionReadyAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var readyTaskObject = method!.Invoke(fixture.ViewModel, ["conv-2", null, CancellationToken.None]);
            var readyTask = Assert.IsAssignableFrom<Task<bool>>(readyTaskObject);

            await Task.Delay(100);
            await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Disconnected));

            var ready = await readyTask;

            Assert.True(ready);
            commands.Verify(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-2", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task SwitchConversationAsync_RemoteBoundConversation_WhenRemoteHydrationFails_ReturnsFalse()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("remote load failed"));

        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected connect"));

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio });
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-2",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        fixture.ViewModel.ReplaceChatService(chatService.Object);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

        var switched = await fixture.ViewModel.SwitchConversationAsync("conv-2");

        Assert.False(switched);
        Assert.Contains("remote load failed", fixture.ViewModel.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        chatService.Verify(
            service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters => string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ActivateConversationAsync_DoesNotCompleteBeforeBufferedReplayProjectsToUi()
    {
        var syncContext = new QueueingSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        var restoreTask = fixture.ViewModel.RestoreAsync();
        await syncContext.RunUntilCompletedAsync(restoreTask);

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-2",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = (parameters, _) =>
            {
                if (string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal))
                {
                    innerChatService!.RaiseSessionUpdate(new SessionUpdateEventArgs(
                        "remote-2",
                        new AgentMessageUpdate(new TextContentBlock("replayed remote history"))));
                }

                return Task.FromResult(SessionLoadResponse.Completed);
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();

        var activationTask = InvokePrivateTaskAsync(fixture.ViewModel, "ActivateConversationAsync", "conv-2", CancellationToken.None);

        while (!activationTask.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        await activationTask;

        var replayStillPendingAfterActivation = syncContext.PendingCount > 0;
        var overlayDismissedBeforeTranscriptProjected =
            !fixture.ViewModel.IsOverlayVisible && fixture.ViewModel.MessageHistory.Count == 0;

        Assert.False(
            replayStillPendingAfterActivation && overlayDismissedBeforeTranscriptProjected,
            "Session activation should not finish with loading hidden while replayed remote history is still waiting to project into the chat UI.");
    }

    [Fact]
    public async Task ActivateConversationAsync_SwitchingBackToPreviouslyHydratedRemoteConversation_ReloadsRemoteSession()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\one");
        await sessionManager.Object.CreateSessionAsync("conv-2", @"C:\repo\two");

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-2",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await fixture.DispatchConnectionAsync(new SetSelectedProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

        Assert.Equal("conv-1", (await fixture.GetStateAsync()).HydratedConversationId);

        var initialHydrated = await fixture.ViewModel.HydrateActiveConversationAsync();
        Assert.True(initialHydrated);

        await InvokePrivateTaskAsync(fixture.ViewModel, "ActivateConversationAsync", "conv-2", CancellationToken.None);
        await InvokePrivateTaskAsync(fixture.ViewModel, "ActivateConversationAsync", "conv-1", CancellationToken.None);

        chatService.Verify(
            service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)
                    && string.Equals(parameters.Cwd, @"C:\repo\one", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        chatService.Verify(
            service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)
                    && string.Equals(parameters.Cwd, @"C:\repo\two", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SelectedMode_SetByUser_SendsConfigOptionRequestAndProjectsReturnedModeState()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        SessionSetConfigOptionParams? capturedParams = null;

        chatService.Setup(service => service.SetSessionConfigOptionAsync(It.IsAny<SessionSetConfigOptionParams>()))
            .Callback<SessionSetConfigOptionParams>(value => capturedParams = value)
            .ReturnsAsync(new SessionSetConfigOptionResponse(CreateModeConfigOptions("plan")));

        viewModel.ReplaceChatService(chatService.Object);

        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        };
        await fixture.UpdateStateAsync(_ => initialState);
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new ConfigOptionUpdate
            {
                ConfigOptions = CreateModeConfigOptions("agent")
            }));

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                viewModel.AvailableModes.Count == 2
                && string.Equals(viewModel.SelectedMode?.ModeId, "agent", StringComparison.Ordinal));
        }, timeoutMilliseconds: 5000);

        var targetMode = Assert.Single(viewModel.AvailableModes.Where(mode => string.Equals(mode.ModeId, "plan", StringComparison.Ordinal)));
        viewModel.SelectedMode = targetMode;

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                capturedParams is not null
                && string.Equals(viewModel.SelectedMode?.ModeId, "plan", StringComparison.Ordinal)
                && string.Equals(viewModel.ConfigOptions[0].Value?.ToString(), "plan", StringComparison.Ordinal));
        }, timeoutMilliseconds: 5000);

        Assert.NotNull(capturedParams);
        Assert.Equal("remote-1", capturedParams!.SessionId);
        Assert.Equal("mode", capturedParams.ConfigId);
        Assert.Equal("plan", capturedParams.Value);
        Assert.Equal("plan", viewModel.SelectedMode?.ModeId);
        Assert.Equal("plan", viewModel.ConfigOptions[0].Value);
    }

    [Fact]
    public async Task DisconnectCommand_DoesNotClearPersistedConversationSessionState()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>();
        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            AvailableModes = ImmutableList.Create(
                new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" },
                new ConversationModeOptionSnapshot { ModeId = "plan", ModeName = "Plan" }),
            SelectedModeId = "agent",
            ConfigOptions = ImmutableList.Create(
                new ConversationConfigOptionSnapshot { Id = "mode", Name = "Mode", SelectedValue = "agent" }),
            ShowConfigOptionsPanel = true
        });

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)
                && fixture.ViewModel.AvailableModes.Count == 2
                && string.Equals(fixture.ViewModel.SelectedMode?.ModeId, "agent", StringComparison.Ordinal));
        });

        await fixture.ViewModel.DisconnectCommand.ExecuteAsync(null);

        var state = await fixture.GetStateAsync();
        Assert.NotNull(state.AvailableModes);
        Assert.Equal(2, state.AvailableModes!.Count);
        Assert.Equal("agent", state.SelectedModeId);
        Assert.NotNull(state.ConfigOptions);
        Assert.Single(state.ConfigOptions!);
        Assert.True(state.ShowConfigOptionsPanel);
    }

    [Fact]
    public async Task AskUserRequestReceived_SubmitAnswer_DisablesPromptUntilResolvedAndClearsPendingState()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        IReadOnlyDictionary<string, string>? capturedAnswers = null;

        chatService.Setup(service => service.RespondToAskUserRequestAsync(It.IsAny<object>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .ThrowsAsync(new InvalidOperationException("Ask-user responses should use the event responder."));

        viewModel.ReplaceChatService(chatService.Object);

        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        };
        await fixture.UpdateStateAsync(_ => initialState);

        await WaitForConditionAsync(() =>
            Task.FromResult(
                string.Equals(viewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)
                && string.Equals(viewModel.CurrentRemoteSessionId, "remote-1", StringComparison.Ordinal)));

        chatService.Raise(
            service => service.AskUserRequestReceived += null,
            new AskUserRequestEventArgs(
                "msg-1",
                new AskUserRequest
                {
                    SessionId = "remote-1",
                    Questions =
                    {
                        new AskUserQuestion
                        {
                            Header = "Execution",
                            Question = "Choose a mode",
                            MultiSelect = false,
                            Options =
                            {
                                new AskUserOption { Label = "Plan", Description = "Planning mode" },
                                new AskUserOption { Label = "Agent", Description = "Interactive mode" }
                            }
                        }
                    }
                },
                answers =>
                {
                    capturedAnswers = new Dictionary<string, string>(answers, StringComparer.Ordinal);
                    return Task.FromResult(true);
                }));

        await WaitForConditionAsync(() =>
        {
            return Task.FromResult(
                viewModel.PendingAskUserRequest is not null
                && !viewModel.IsInputEnabled);
        });

        var askUserRequest = viewModel.PendingAskUserRequest;
        Assert.NotNull(askUserRequest);
        var question = Assert.Single(askUserRequest!.Questions);
        question.Options[0].ToggleSelectedCommand.Execute(null);
        await askUserRequest.SubmitCommand.ExecuteAsync(null);

        await WaitForConditionAsync(() =>
        {
            return Task.FromResult(
                viewModel.PendingAskUserRequest is null
                && viewModel.IsInputEnabled
                && capturedAnswers is not null);
        });

        Assert.NotNull(capturedAnswers);
        Assert.Equal("Plan", capturedAnswers!["Choose a mode"]);
        chatService.Verify(
            service => service.RespondToAskUserRequestAsync(It.IsAny<object>(), It.IsAny<IReadOnlyDictionary<string, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ProjectAffinityCorrection_RemoteBoundUnclassifiedConversation_ShowsCorrectionAffordance()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        fixture.Preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = "project-1",
            Name = "Project One",
            RootPath = @"C:\Repo\One"
        });

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.ViewModel.IsProjectAffinityCorrectionVisible);
        });

        Assert.Equal(NavigationProjectIds.Unclassified, fixture.ViewModel.EffectiveProjectAffinityProjectId);
        Assert.Equal(ProjectAffinitySource.Unclassified, fixture.ViewModel.EffectiveProjectAffinitySource);
        Assert.False(fixture.ViewModel.HasProjectAffinityOverride);
        var option = Assert.Single(fixture.ViewModel.ProjectAffinityOverrideOptions);
        Assert.Equal("project-1", option.ProjectId);
    }

    [Fact]
    public async Task ApplyProjectAffinityOverrideCommand_SetsWorkspaceOverride_AndProjectsEffectiveOverride()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        fixture.Preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = "project-1",
            Name = "Project One",
            RootPath = @"C:\Repo\One"
        });

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.ViewModel.IsProjectAffinityCorrectionVisible);
        });

        fixture.ViewModel.SelectedProjectAffinityOverrideProjectId = "project-1";
        fixture.ViewModel.ApplyProjectAffinityOverrideCommand.Execute(null);
        syncContext.RunAll();

        Assert.Equal("project-1", fixture.Workspace.GetProjectAffinityOverride("conv-1")?.ProjectId);
        Assert.True(fixture.ViewModel.HasProjectAffinityOverride);
        Assert.Equal("project-1", fixture.ViewModel.EffectiveProjectAffinityProjectId);
        Assert.Equal(ProjectAffinitySource.Override, fixture.ViewModel.EffectiveProjectAffinitySource);
    }

    [Fact]
    public async Task ClearProjectAffinityOverrideCommand_ClearsWorkspaceOverride_AndRestoresUnclassifiedEffectiveAffinity()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        fixture.Preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = "project-1",
            Name = "Project One",
            RootPath = @"C:\Repo\One"
        });

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.ViewModel.IsProjectAffinityCorrectionVisible);
        });

        fixture.ViewModel.SelectedProjectAffinityOverrideProjectId = "project-1";
        fixture.ViewModel.ApplyProjectAffinityOverrideCommand.Execute(null);
        syncContext.RunAll();

        fixture.ViewModel.ClearProjectAffinityOverrideCommand.Execute(null);
        syncContext.RunAll();

        Assert.Null(fixture.Workspace.GetProjectAffinityOverride("conv-1"));
        Assert.False(fixture.ViewModel.HasProjectAffinityOverride);
        Assert.Equal(NavigationProjectIds.Unclassified, fixture.ViewModel.EffectiveProjectAffinityProjectId);
        Assert.Equal(ProjectAffinitySource.Unclassified, fixture.ViewModel.EffectiveProjectAffinitySource);
        Assert.True(fixture.ViewModel.IsProjectAffinityCorrectionVisible);
    }

    [Fact]
    public async Task ProjectAffinityOverride_RestoreAndNavigationRebuild_KeepsOverrideProjected()
    {
        var syncContext = new QueueingSynchronizationContext();
        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument
            {
                LastActiveConversationId = "conv-1",
                Conversations =
                {
                    new ConversationRecord
                    {
                        ConversationId = "conv-1",
                        DisplayName = "Conversation One",
                        CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedAt = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                        RemoteSessionId = "remote-1",
                        BoundProfileId = "profile-1",
                        ProjectAffinityOverrideProjectId = "project-1"
                    }
                }
            });

        await using var fixture = CreateViewModel(syncContext, conversationStore: conversationStore);
        fixture.Preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = "project-1",
            Name = "Project One",
            RootPath = @"C:\Repo\One"
        });

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());
        syncContext.RunAll();

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        syncContext.RunAll();

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.ViewModel.HasProjectAffinityOverride);
        });

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-2" });
        syncContext.RunAll();

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-1" });

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                fixture.ViewModel.HasProjectAffinityOverride
                && string.Equals(fixture.ViewModel.EffectiveProjectAffinityProjectId, "project-1", StringComparison.Ordinal)
                && fixture.ViewModel.EffectiveProjectAffinitySource == ProjectAffinitySource.Override);
        });

        Assert.Equal("project-1", fixture.Workspace.GetProjectAffinityOverride("conv-1")?.ProjectId);
    }

    private static bool IsUserFriendlyHydrationOverlayStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return (status.StartsWith("正在", StringComparison.Ordinal) || status.StartsWith("即将", StringComparison.Ordinal))
            && (status.Contains("聊天", StringComparison.Ordinal) || status.Contains("消息", StringComparison.Ordinal))
            && !status.Contains("ACP", StringComparison.OrdinalIgnoreCase)
            && !status.Contains("协议", StringComparison.Ordinal);
    }
}
