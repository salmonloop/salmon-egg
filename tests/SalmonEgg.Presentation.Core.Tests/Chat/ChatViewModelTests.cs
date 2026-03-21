using System;
using System.Collections;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
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
        Mock<ISessionManager>? sessionManager = null)
    {
        var state = State.Value(new object(), () => ChatState.Empty);
        var connectionState = State.Value(new object(), () => ChatConnectionState.Empty);
        var connectionStore = new ChatConnectionStore(connectionState);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action => state.Update(s => ChatReducer.Reduce(s!, action), default));
        var transportFactory = new Mock<ITransportFactory>();
        var messageParser = new Mock<IMessageParser>();
        var messageValidator = new Mock<IMessageValidator>();
        var errorLogger = new Mock<IErrorLogger>();
        var capabilityManager = new Mock<ICapabilityManager>();
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
            capabilityManager.Object,
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
                chatStore.Object,
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
                acpConnectionCommands);
            return new ViewModelFixture(
                viewModel,
                state,
                connectionState,
                connectionStore,
                chatStore.Object,
                workspace,
                conversationStore,
                preferences,
                profiles,
                chatStore);
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

        var restoreTask = fixture.ViewModel.RestoreAsync();
        await syncContext.RunUntilCompletedAsync(restoreTask);
        syncContext.RunAll();

        conversationStore.Verify(s => s.LoadAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(new[] { "session-1" }, fixture.Workspace.GetKnownConversationIds());

        var workspaceBinding = fixture.Workspace.GetRemoteBinding("session-1");
        Assert.Equal("remote-1", workspaceBinding?.RemoteSessionId);
        Assert.Equal("profile-a", workspaceBinding?.BoundProfileId);

        var dispatchedActions = fixture.ChatStore.Invocations
            .Where(invocation => string.Equals(invocation.Method.Name, nameof(IChatStore.Dispatch), StringComparison.Ordinal))
            .SelectMany(invocation => invocation.Arguments)
            .OfType<ChatAction>()
            .ToArray();
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
        Assert.Equal("session-1", fixture.ViewModel.CurrentSessionId);
        Assert.Equal("remote-1", fixture.ViewModel.CurrentRemoteSessionId);
        Assert.Single(fixture.ViewModel.MessageHistory);
        Assert.Equal("hello from restore", fixture.ViewModel.MessageHistory[0].TextContent);
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
    public async Task CreateNewSessionCommand_CreatesLocalConversationWithoutBinding()
    {
        await using var fixture = CreateViewModel();
        fixture.Preferences.LastSelectedServerId = "profile-1";

        var chatService = new Mock<IChatService>();
        chatService.Setup(s => s.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-1"));
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.ViewModel.CreateNewSessionCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.CurrentSessionId));
        Assert.NotEqual("remote-1", fixture.ViewModel.CurrentSessionId);
        Assert.Null(fixture.ViewModel.CurrentRemoteSessionId);

        var state = await fixture.GetStateAsync();
        Assert.Null(state.SelectedConversationId);
        Assert.Equal(fixture.ViewModel.CurrentSessionId, state.HydratedConversationId);

        var remoteBinding = fixture.Workspace.GetRemoteBinding(fixture.ViewModel.CurrentSessionId!);
        Assert.Null(remoteBinding);
    }

    [Fact]
    public async Task CreateNewSessionCommand_DoesNotPromoteConversationSelectionIntoChatState()
    {
        await using var fixture = CreateViewModel();
        fixture.Preferences.LastSelectedServerId = "profile-1";

        var chatService = new Mock<IChatService>();
        chatService.Setup(s => s.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-1"));
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.ViewModel.CreateNewSessionCommand.ExecuteAsync(null);

        Assert.Null((await fixture.GetStateAsync()).SelectedConversationId);
    }

    [Fact]
    public async Task CreateNewSessionCommand_DoesNotOwnStoreSelectedConversationId()
    {
        await using var fixture = CreateViewModel();
        var chatService = new Mock<IChatService>();
        chatService.Setup(s => s.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-1"));
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.ViewModel.CreateNewSessionCommand.ExecuteAsync(null);

        var state = await fixture.GetStateAsync();
        Assert.Null(state.SelectedConversationId);
    }

    [Fact]
    public async Task StoreSelectedConversationId_DoesNotOverrideCurrentSessionIdProjection()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        syncContext.RunAll();

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await Task.Delay(50);
        syncContext.RunAll();

        await fixture.UpdateStateAsync(state => state with { SelectedConversationId = "store-selection" });
        await Task.Delay(50);
        syncContext.RunAll();

        var state = await fixture.GetStateAsync();
        Assert.Equal("store-selection", state.SelectedConversationId);
        Assert.Equal("session-1", fixture.ViewModel.CurrentSessionId);
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
        Assert.Null(state.SelectedConversationId);
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
        Assert.Null(state.SelectedConversationId);
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
    public async Task SelectedProfileId_DoesNotFallBackToUiSelection_WhenStoreSelectionIsMissing()
    {
        await using var fixture = CreateViewModel();
        var profile = new ServerConfiguration { Id = "profile-a", Name = "Profile A", Transport = TransportType.Stdio };
        fixture.Profiles.Profiles.Add(profile);
        var suppressField = typeof(ChatViewModel)
            .GetField("_suppressStoreProfileProjection", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(suppressField);
        suppressField!.SetValue(fixture.ViewModel, true);

        try
        {
            fixture.ViewModel.SelectedAcpProfile = profile;
            await Task.Delay(50);
        }
        finally
        {
            suppressField.SetValue(fixture.ViewModel, false);
        }

        Assert.Null(fixture.ViewModel.SelectedProfileId);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstTask);
        Assert.Equal(1, connectCalls);

        await fixture.ViewModel.TryAutoConnectAsync(CancellationToken.None);

        Assert.Equal(2, connectCalls);
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
        var initialState = ChatState.Empty with { IsThinking = false };
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
        var capabilityManager = new Mock<ICapabilityManager>();
        var sessionManager = new Mock<ISessionManager>();
        var serilog = new Mock<Serilog.ILogger>();

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            capabilityManager.Object,
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
        Assert.False(viewModel.IsThinking);

        viewModel.Dispose();

        var newState = initialState with { IsThinking = true };
        await state.Update(_ => newState, CancellationToken.None);

        syncContext.RunAll();
        Assert.False(viewModel.IsThinking);
    }

    [Fact]
    public async Task Dispose_DropsAlreadyQueuedStoreProjection()
    {
        var initialState = ChatState.Empty with { IsThinking = false };
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
        var capabilityManager = new Mock<ICapabilityManager>();
        var sessionManager = new Mock<ISessionManager>();
        var serilog = new Mock<Serilog.ILogger>();

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            capabilityManager.Object,
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
        Assert.False(viewModel.IsThinking);

        await state.Update(_ => initialState with { IsThinking = true }, CancellationToken.None);
        viewModel.Dispose();

        syncContext.RunAll();
        Assert.False(viewModel.IsThinking);
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
        var capabilityManager = new Mock<ICapabilityManager>();
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
            capabilityManager.Object,
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

        public int PendingCount => _work.Count;

        public override void Post(SendOrPostCallback d, object? state)
        {
            _work.Enqueue((d, state));
        }

        public void RunAll()
        {
            while (_work.Count > 0)
            {
                var (callback, state) = _work.Dequeue();
                callback(state);
            }
        }

        public async Task RunUntilCompletedAsync(Task task, int spinDelayMs = 10)
        {
            while (!task.IsCompleted)
            {
                if (_work.Count == 0)
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

    private static void AssertNoLegacyConnectionActionsDispatched(Mock<IChatStore> chatStore)
    {
        var dispatchedActions = chatStore.Invocations
            .Where(invocation => string.Equals(invocation.Method.Name, nameof(IChatStore.Dispatch), StringComparison.Ordinal))
            .SelectMany(invocation => invocation.Arguments)
            .OfType<ChatAction>();

        Assert.DoesNotContain(dispatchedActions, action =>
            action is SetConnectionLifecycleAction
            or UpdateConnectionStatusAction
            or SetAuthenticationStateAction);
    }

    private sealed class ViewModelFixture : IDisposable, IAsyncDisposable
    {
        private readonly IState<ChatState> _state;
        private readonly IState<ChatConnectionState> _connectionState;
        private readonly IChatConnectionStore _connectionStore;
        private readonly IChatStore _store;
        private readonly ChatConversationWorkspace _workspace;
        private readonly Mock<IChatStore> _chatStore;
        public ChatViewModel ViewModel { get; }
        public Mock<IConversationStore> ConversationStore { get; }
        public ChatConversationWorkspace Workspace => _workspace;
        public AppPreferencesViewModel Preferences { get; }
        public AcpProfilesViewModel Profiles { get; }
        public Mock<IChatStore> ChatStore => _chatStore;

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
            Mock<IChatStore> chatStore)
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
        }

        public async Task<ChatState> GetStateAsync() => await _state ?? ChatState.Empty;

        public async Task<ChatConnectionState> GetConnectionStateAsync() => await _connectionState ?? ChatConnectionState.Empty;

        public ValueTask DispatchAsync(ChatAction action) => _store.Dispatch(action);

        public ValueTask DispatchConnectionAsync(ChatConnectionAction action) => _connectionStore.Dispatch(action);

        public ValueTask UpdateStateAsync(Func<ChatState, ChatState> update)
            => _state.Update(current => update(current ?? ChatState.Empty), CancellationToken.None);

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
}
