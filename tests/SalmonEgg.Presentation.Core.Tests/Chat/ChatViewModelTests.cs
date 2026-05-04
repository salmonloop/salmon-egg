using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.ConversationPreview;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SerilogLogger = Serilog.ILogger;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public partial class ChatViewModelTests
{
    private static ViewModelFixture CreateViewModel(
        SynchronizationContext? syncContext = null,
        Mock<IConversationStore>? conversationStore = null,
        IAcpConnectionCommands? acpConnectionCommands = null,
        Mock<IConfigurationService>? configurationService = null,
        Mock<ISessionManager>? sessionManager = null,
        IConversationActivationCoordinator? conversationActivationCoordinator = null,
        Func<IChatConnectionStore, IAcpConnectionCoordinator>? acpConnectionCoordinatorFactory = null,
        IConversationBindingCommands? bindingCommands = null,
        IVoiceInputService? voiceInputService = null,
        IConversationPreviewStore? previewStore = null,
        IShellNavigationRuntimeState? shellNavigationRuntimeState = null,
        LocalTerminalPanelCoordinator? localTerminalPanelCoordinator = null,
        IAcpConnectionSessionRegistry? connectionSessionRegistry = null)
    {
        var state = State.Value(new object(), () => ChatState.Empty);
        var connectionState = State.Value(new object(), () => ChatConnectionState.Empty);
        var attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
        var attentionStore = new ConversationAttentionStore(attentionState);
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
        var uiDispatcher = syncContext as IUiDispatcher ?? new ImmediateUiDispatcher();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object,
            uiDispatcher);

        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configurationService.Object, preferences, profilesLogger.Object, new ImmediateUiDispatcher());

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
            uiDispatcher);
        var conversationCatalogPresenter = new ConversationCatalogPresenter();
        var conversationCatalogFacade = new ConversationCatalogFacade(
            workspace,
            new NavigationProjectPreferencesAdapter(preferences),
            conversationActivationCoordinator ?? Mock.Of<IConversationActivationCoordinator>(),
            Mock.Of<IShellSelectionReadModel>(),
            new Lazy<INavigationCoordinator>(() => Mock.Of<INavigationCoordinator>()),
            conversationCatalogPresenter,
            NullLogger<ConversationCatalogFacade>.Instance);
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
                uiDispatcher,
                previewStore ?? Mock.Of<IConversationPreviewStore>(),
                vmLogger.Object,
                conversationAttentionStore: attentionStore,
                acpConnectionCommands: acpConnectionCommands,
                conversationActivationCoordinator: conversationActivationCoordinator,
                bindingCommands: bindingCommands,
                acpConnectionCoordinator: acpConnectionCoordinatorFactory?.Invoke(connectionStore),
                shellNavigationRuntimeState: shellNavigationRuntimeState,
                voiceInputService: voiceInputService,
                localTerminalPanelCoordinator: localTerminalPanelCoordinator,
                conversationCatalogFacade: conversationCatalogFacade,
                connectionSessionRegistry: connectionSessionRegistry);
            conversationCatalogFacade.SetPanelCleanup(viewModel);
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
                attentionState,
                chatStore,
                vmLogger);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static ShellLayoutViewModel CreateShellLayoutViewModel()
    {
        var initialState = ShellLayoutState.Default;
        var initialSnapshot = ShellLayoutPolicy.Compute(initialState);
        var state = State.Value(new object(), () => initialState);
        var snapshot = State.Value(new object(), () => initialSnapshot);
        var store = new ShellLayoutStore(state, snapshot, initialState, initialSnapshot);
        return new ShellLayoutViewModel(store, new ImmediateUiDispatcher());
    }

    private static DisplayCatalogTestScope CreateDisplayCatalogPresenter(
        IConversationCatalogReadModel catalog,
        IUiDispatcher uiDispatcher)
        => new(catalog, uiDispatcher);

    private static void SetCurrentRemoteSessionId(ChatViewModel viewModel, string? remoteSessionId)
    {
        var field = typeof(ChatViewModel).GetField("_currentRemoteSessionId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(viewModel, remoteSessionId);
    }

    private static void SetCurrentSessionId(ChatViewModel viewModel, string? conversationId)
    {
        var field = typeof(ChatViewModel).GetField("_currentSessionId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(viewModel, conversationId);
    }

    private static ConversationCatalogPresenter GetConversationCatalogPresenter(ChatViewModel viewModel)
    {
        var field = typeof(ChatViewModel).GetField("_conversationCatalogPresenter", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<ConversationCatalogPresenter>(field!.GetValue(viewModel));
    }

    private static T? GetPrivateFieldValue<T>(ChatViewModel viewModel, string fieldName)
    {
        var field = typeof(ChatViewModel).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T?)field!.GetValue(viewModel);
    }

    private static void WireViewModelCoordinatorIntoFacade(ChatViewModel viewModel)
    {
        var coordinator = Assert.IsType<ConversationActivationCoordinator>(
            GetPrivateFieldValue<object>(viewModel, "_conversationActivationCoordinator"));
        var facade = Assert.IsType<ConversationCatalogFacade>(
            GetPrivateFieldValue<object>(viewModel, "_conversationCatalogFacade"));
        typeof(ConversationCatalogFacade)
            .GetField("_activationCoordinator", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(facade, coordinator);
    }

    private static Mock<ISessionManager> CreateSessionManagerWithStore()
    {
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
                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(sessionId => sessions.Remove(sessionId));
        return sessionManager;
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
    public async Task ChatShellViewModel_SelectedMiniWindowSession_WhenRemoteConversationSelected_ActivatesCoordinatorWithResolvedProjectId()
    {
        await using var fixture = CreateViewModel();
        await fixture.ViewModel.RestoreAsync();

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript: [],
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
        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-local" });

        var catalog = new ConversationCatalogPresenter();
        catalog.Refresh(
        [
            new ConversationCatalogItem(
                "conv-remote",
                "Remote Conversation",
                @"C:\repo\remote",
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                RemoteSessionId: "remote-1",
                BoundProfileId: "profile-1")
        ]);

        var resolver = new Mock<IProjectAffinityResolver>(MockBehavior.Strict);
        resolver.Setup(x => x.Resolve(It.Is<ProjectAffinityRequest>(request =>
                string.Equals(request.RemoteCwd, @"C:\repo\remote", StringComparison.Ordinal)
                && string.Equals(request.BoundProfileId, "profile-1", StringComparison.Ordinal)
                && string.Equals(request.RemoteSessionId, "remote-1", StringComparison.Ordinal)
                && string.Equals(request.UnclassifiedProjectId, NavigationProjectIds.Unclassified, StringComparison.Ordinal))))
            .Returns(new ProjectAffinityResolution(
                "project-remote",
                ProjectAffinitySource.DirectMatch,
                "project-remote",
                null,
                @"C:\repo\remote",
                @"C:\repo\remote",
                false,
                "matched"));

        var activated = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var navigationCoordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        navigationCoordinator.Setup(x => x.ActivateSessionAsync("conv-remote", "project-remote"))
            .Returns(() =>
            {
                activated.TrySetResult(null);
                return Task.FromResult(true);
            });

        using var shellLayout = CreateShellLayoutViewModel();
        await using var displayCatalog = CreateDisplayCatalogPresenter(catalog, fixture.ViewModel.Dispatcher);
        var shellViewModel = new ChatShellViewModel(
            fixture.ViewModel,
            shellLayout,
            navigationCoordinator.Object,
            displayCatalog.Presenter,
            resolver.Object,
            fixture.Preferences,
            Mock.Of<ILogger<ChatShellViewModel>>());

        var remoteItem = fixture.ViewModel.MiniWindowSessions.Single(item =>
            string.Equals(item.ConversationId, "conv-remote", StringComparison.Ordinal));

        shellViewModel.SelectedMiniWindowSession = remoteItem;
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(2));

        navigationCoordinator.Verify(x => x.ActivateSessionAsync("conv-remote", "project-remote"), Times.Once);
    }

    [Fact]
    public async Task ChatShellViewModel_SelectedMiniWindowSession_WhenCurrentConversationAlreadySelected_IgnoresDuplicateIntent()
    {
        await using var fixture = CreateViewModel();
        await fixture.ViewModel.RestoreAsync();

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));
        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-remote" });

        var catalog = new ConversationCatalogPresenter();
        catalog.Refresh(
        [
            new ConversationCatalogItem(
                "conv-remote",
                "Remote Conversation",
                @"C:\repo\remote",
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc))
        ]);

        var resolver = new Mock<IProjectAffinityResolver>(MockBehavior.Strict);
        var navigationCoordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);

        using var shellLayout = CreateShellLayoutViewModel();
        await using var displayCatalog = CreateDisplayCatalogPresenter(catalog, fixture.ViewModel.Dispatcher);
        var shellViewModel = new ChatShellViewModel(
            fixture.ViewModel,
            shellLayout,
            navigationCoordinator.Object,
            displayCatalog.Presenter,
            resolver.Object,
            fixture.Preferences,
            Mock.Of<ILogger<ChatShellViewModel>>());

        Assert.NotNull(shellViewModel.SelectedMiniWindowSession);
        Assert.Equal("conv-remote", shellViewModel.SelectedMiniWindowSession!.ConversationId);

        shellViewModel.SelectedMiniWindowSession =
            new MiniWindowConversationItemViewModel("conv-remote", "Remote Conversation", "Remote Conversation");

        await Task.Delay(50);

        navigationCoordinator.Verify(x => x.ActivateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        resolver.Verify(x => x.Resolve(It.IsAny<ProjectAffinityRequest>()), Times.Never);
    }

    [Fact]
    public async Task StartVoiceInputCommand_WhenPermissionDenied_DoesNotStartAndExposesError()
    {
        var voiceInput = new FakeVoiceInputService
        {
            IsSupported = true,
            PermissionResult = new VoiceInputPermissionResult(VoiceInputPermissionStatus.Denied, "Microphone denied")
        };

        await using var fixture = CreateViewModel(voiceInputService: voiceInput);
        fixture.ViewModel.IsSessionActive = true;

        await fixture.ViewModel.StartVoiceInputCommand.ExecuteAsync(null);

        Assert.False(fixture.ViewModel.IsVoiceInputListening);
        Assert.Equal(0, voiceInput.StartCount);
        Assert.Contains("Microphone denied", fixture.ViewModel.VoiceInputErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartVoiceInputCommand_WhenVoiceInputIsUnsupported_DoesNotRequestPermissionOrStart()
    {
        var voiceInput = new FakeVoiceInputService
        {
            IsSupported = false,
            PermissionResult = VoiceInputPermissionResult.Granted()
        };

        await using var fixture = CreateViewModel(voiceInputService: voiceInput);
        fixture.ViewModel.IsSessionActive = true;

        Assert.False(fixture.ViewModel.CanStartVoiceInput);

        await fixture.ViewModel.StartVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal(0, voiceInput.PermissionRequestCount);
        Assert.Equal(0, voiceInput.StartCount);
        Assert.False(fixture.ViewModel.IsVoiceInputListening);
    }

    [Fact]
    public async Task VoiceInputVisibility_WhenVoiceInputIsUnsupported_HidesBothButtons()
    {
        var voiceInput = new FakeVoiceInputService
        {
            IsSupported = false
        };

        await using var fixture = CreateViewModel(voiceInputService: voiceInput);

        Assert.False(fixture.ViewModel.IsVoiceInputSupported);
        Assert.False(fixture.ViewModel.ShowVoiceInputStartButton);
        Assert.False(fixture.ViewModel.ShowVoiceInputStopButton);
    }

    [Fact]
    public async Task VoiceInputFinalResult_UpdatesPrompt_AndKeepsListeningUntilStopped()
    {
        var voiceInput = new FakeVoiceInputService
        {
            IsSupported = true,
            PermissionResult = VoiceInputPermissionResult.Granted()
        };

        await using var fixture = CreateViewModel(voiceInputService: voiceInput);
        fixture.ViewModel.IsSessionActive = true;
        fixture.ViewModel.CurrentPrompt = "hello";

        await fixture.ViewModel.StartVoiceInputCommand.ExecuteAsync(null);

        Assert.True(fixture.ViewModel.IsVoiceInputListening);
        Assert.Equal(1, voiceInput.StartCount);

        var requestId = Assert.Single(voiceInput.StartedSessionIds);
        voiceInput.EmitFinal(new VoiceInputFinalResult(requestId, "world"));

        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.CurrentPrompt, "hello world", StringComparison.Ordinal)));

        Assert.Equal("hello world", fixture.ViewModel.CurrentPrompt);
        Assert.True(fixture.ViewModel.IsVoiceInputListening);
    }

    [Fact]
    public async Task VoiceInputPartialResult_UpdatesPromptWhileListening()
    {
        var voiceInput = new FakeVoiceInputService
        {
            IsSupported = true,
            PermissionResult = VoiceInputPermissionResult.Granted()
        };

        await using var fixture = CreateViewModel(voiceInputService: voiceInput);
        fixture.ViewModel.IsSessionActive = true;
        fixture.ViewModel.CurrentPrompt = "hello";

        await fixture.ViewModel.StartVoiceInputCommand.ExecuteAsync(null);

        var requestId = Assert.Single(voiceInput.StartedSessionIds);
        voiceInput.EmitPartial(new VoiceInputPartialResult(requestId, "live transcript"));

        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.CurrentPrompt, "hello live transcript", StringComparison.Ordinal)));

        Assert.True(fixture.ViewModel.IsVoiceInputListening);
    }

    [Fact]
    public async Task StopVoiceInputCommand_StopsActiveVoiceSession()
    {
        var voiceInput = new FakeVoiceInputService
        {
            IsSupported = true,
            PermissionResult = VoiceInputPermissionResult.Granted()
        };

        await using var fixture = CreateViewModel(voiceInputService: voiceInput);
        fixture.ViewModel.IsSessionActive = true;

        await fixture.ViewModel.StartVoiceInputCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.IsVoiceInputListening);

        await fixture.ViewModel.StopVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal(1, voiceInput.StopCount);
        Assert.False(fixture.ViewModel.IsVoiceInputListening);
    }

    [Fact]
    public async Task StopVoiceInputCommand_DoesNotCancelCallerTokenBeforeServiceStop()
    {
        var voiceInput = new FakeVoiceInputService
        {
            IsSupported = true,
            PermissionResult = VoiceInputPermissionResult.Granted(),
            ThrowIfStopCalledAfterCallerCancellation = true
        };

        await using var fixture = CreateViewModel(voiceInputService: voiceInput);
        fixture.ViewModel.IsSessionActive = true;

        await fixture.ViewModel.StartVoiceInputCommand.ExecuteAsync(null);
        await fixture.ViewModel.StopVoiceInputCommand.ExecuteAsync(null);

        Assert.Null(fixture.ViewModel.VoiceInputErrorMessage);
        Assert.Equal(1, voiceInput.StopCount);
    }

    [Fact]
    public async Task StartVoiceInputCommand_WhenPermissionDenied_RequestsAuthorizationHelp()
    {
        var voiceInput = new FakeVoiceInputService
        {
            IsSupported = true,
            PermissionResult = new VoiceInputPermissionResult(
                VoiceInputPermissionStatus.Denied,
                "Enable speech access",
                RequiresAuthorization: true)
        };

        await using var fixture = CreateViewModel(voiceInputService: voiceInput);
        fixture.ViewModel.IsSessionActive = true;

        await fixture.ViewModel.StartVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal(1, voiceInput.AuthorizationHelpRequestCount);
    }

    [Fact]
    public async Task VoiceInputError_WhenAuthorizationRequired_RequestsAuthorizationHelp()
    {
        var voiceInput = new FakeVoiceInputService
        {
            IsSupported = true,
            PermissionResult = VoiceInputPermissionResult.Granted()
        };

        await using var fixture = CreateViewModel(voiceInputService: voiceInput);
        fixture.ViewModel.IsSessionActive = true;

        await fixture.ViewModel.StartVoiceInputCommand.ExecuteAsync(null);

        var requestId = Assert.Single(voiceInput.StartedSessionIds);
        voiceInput.EmitError(new VoiceInputErrorResult(
            requestId,
            "Enable speech access",
            ErrorCode: "0x80045509",
            RequiresAuthorization: true));

        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.VoiceInputErrorMessage, "Enable speech access", StringComparison.Ordinal)));

        Assert.Equal(1, voiceInput.AuthorizationHelpRequestCount);
    }

    [Fact]
    public async Task Dispose_DoesNotDisposeInjectedVoiceInputService()
    {
        var voiceInput = new FakeVoiceInputService
        {
            IsSupported = true
        };

        var fixture = CreateViewModel(voiceInputService: voiceInput);

        await fixture.DisposeAsync();

        Assert.Equal(0, voiceInput.DisposeCount);
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
            return string.Equals(connectionState.SettingsSelectedProfileId, "profile-1", StringComparison.Ordinal);
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

        await WaitForConditionAsync(async () =>
        {
            var currentSessionId = fixture.ViewModel.CurrentSessionId;
            var state = await fixture.GetStateAsync();
            return !string.IsNullOrWhiteSpace(currentSessionId)
                && !string.Equals("remote-1", currentSessionId, StringComparison.Ordinal)
                && string.Equals("remote-1", fixture.ViewModel.CurrentRemoteSessionId, StringComparison.Ordinal)
                && string.Equals(currentSessionId, state.HydratedConversationId, StringComparison.Ordinal)
                && string.Equals(
                    "remote-1",
                    state.ResolveBinding(currentSessionId!)?.RemoteSessionId,
                    StringComparison.Ordinal);
        });

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
    public async Task ConnectToAcpProfileCommand_UsesConnectionCommandsDefaultConnectPath()
    {
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        var chatService = new Mock<IChatService>(MockBehavior.Strict);
        chatService.SetupGet(service => service.IsConnected).Returns(true);
        chatService.SetupGet(service => service.IsInitialized).Returns(true);

        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Profile 1",
            Transport = TransportType.Stdio
        };

        commands
            .Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(candidate => ReferenceEquals(candidate, profile)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpTransportApplyResult(
                chatService.Object,
                new InitializeResponse(
                    1,
                    new AgentInfo("agent", "1.0.0"),
                    new AgentCapabilities())));

        await using var fixture = CreateViewModel(acpConnectionCommands: commands.Object);
        fixture.Profiles.Profiles.Add(profile);

        await fixture.ViewModel.ConnectToAcpProfileCommand.ExecuteAsync(profile);

        commands.Verify(x => x.ConnectToProfileAsync(
            It.Is<ServerConfiguration>(candidate => ReferenceEquals(candidate, profile)),
            It.IsAny<IAcpTransportConfiguration>(),
            It.IsAny<IAcpChatCoordinatorSink>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectToAcpProfileCommand_Failure_TransitionsToDisconnectedStoreError()
    {
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Profile 1",
            Transport = TransportType.Stdio
        };

        commands
            .Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(candidate => ReferenceEquals(candidate, profile)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await using var fixture = CreateViewModel(acpConnectionCommands: commands.Object);
        fixture.Profiles.Profiles.Add(profile);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.ViewModel.ConnectToAcpProfileCommand.ExecuteAsync(profile));

        Assert.Equal("boom", exception.Message);

        var connectionState = await fixture.GetConnectionStateAsync();
        Assert.Equal(ConnectionPhase.Disconnected, connectionState.Phase);
        Assert.Equal("boom", connectionState.Error);
        await WaitForConditionAsync(() =>
        {
            return Task.FromResult(
                !fixture.ViewModel.IsInitializing
                && !fixture.ViewModel.IsConnected
                && fixture.ViewModel.HasConnectionError
                && string.Equals(fixture.ViewModel.ConnectionErrorMessage, "boom", StringComparison.Ordinal)
                && string.Equals(fixture.ViewModel.CurrentConnectionStatus, "Disconnected", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_BackgroundAgentMessage_MarksUnreadAttentionOnBoundConversation()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await RunWithSynchronizationContextAsync(syncContext, async () =>
        {
            await using var fixture = CreateViewModel(syncContext);
            var chatService = CreateConnectedChatService();
            await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-foreground",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-foreground", new ConversationBindingSlice("conv-foreground", "remote-foreground", "profile-1"))
                    .Add("conv-background", new ConversationBindingSlice("conv-background", "remote-background", "profile-1"))
            });
            await WaitForConditionAsync(() =>
                Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-foreground", StringComparison.Ordinal)));

            chatService.Raise(
                service => service.SessionUpdateReceived += null,
                new SessionUpdateEventArgs("remote-background", new AgentMessageUpdate(new TextContentBlock("background update"))));

            await WaitForConditionAsync(async () =>
            {
                var attentionState = await fixture.GetAttentionStateAsync();
                return HasUnreadAttention(attentionState, "conv-background");
            });

            var finalAttentionState = await fixture.GetAttentionStateAsync();
            Assert.True(HasUnreadAttention(finalAttentionState, "conv-background"));
            Assert.False(HasUnreadAttention(finalAttentionState, "conv-foreground"));
        });
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_BackgroundAgentMessage_DoesNotRouteWorkspaceOnlyBinding()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await RunWithSynchronizationContextAsync(syncContext, async () =>
        {
            await using var fixture = CreateViewModel(syncContext);
            var chatService = CreateConnectedChatService();
            await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-foreground",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-background",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpdateRemoteBinding("conv-foreground", "remote-foreground", "profile-1");
            fixture.Workspace.UpdateRemoteBinding("conv-background", "remote-background", "profile-1");

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-foreground",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-foreground", new ConversationBindingSlice("conv-foreground", "remote-foreground", "profile-1"))
            });

            chatService.Raise(
                service => service.SessionUpdateReceived += null,
                new SessionUpdateEventArgs("remote-background", new AgentMessageUpdate(new TextContentBlock("background update"))));

            await Task.Delay(200);

            var finalAttentionState = await fixture.GetAttentionStateAsync();
            Assert.False(HasUnreadAttention(finalAttentionState, "conv-background"));
            Assert.False(HasUnreadAttention(finalAttentionState, "conv-foreground"));
        });
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_AgentMessage_DoesNotRouteFromStaleCurrentRemoteSessionState()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await RunWithSynchronizationContextAsync(syncContext, async () =>
        {
            await using var fixture = CreateViewModel(syncContext);
            var chatService = CreateConnectedChatService();
            await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-active",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
            });

            SetCurrentSessionId(fixture.ViewModel, "conv-active");
            SetCurrentRemoteSessionId(fixture.ViewModel, "remote-stale");

            chatService.Raise(
                service => service.SessionUpdateReceived += null,
                new SessionUpdateEventArgs("remote-stale", new AgentMessageUpdate(new TextContentBlock("stale update"))));

            await Task.Delay(200);

            Assert.Empty(fixture.ViewModel.MessageHistory);
            var finalAttentionState = await fixture.GetAttentionStateAsync();
            Assert.False(HasUnreadAttention(finalAttentionState, "conv-active"));
        });
    }

    [Fact]
    public async Task SwitchConversationAsync_ClearsUnreadAttentionWhenConversationIsHydrated()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await RunWithSynchronizationContextAsync(syncContext, async () =>
        {
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

            await sessionManager.Object.CreateSessionAsync("conv-foreground", @"C:\repo\foreground");
            await sessionManager.Object.CreateSessionAsync("conv-background", @"C:\repo\background");

            await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-foreground",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-background",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-foreground",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-foreground", new ConversationBindingSlice("conv-foreground", "remote-foreground", "profile-1"))
                    .Add("conv-background", new ConversationBindingSlice("conv-background", "remote-background", "profile-1"))
            });

            await WaitForConditionAsync(() =>
                Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-foreground", StringComparison.Ordinal)));

            var backgroundChatService = CreateConnectedChatService();
            backgroundChatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
            backgroundChatService.Setup(service => service.LoadSessionAsync(
                    It.IsAny<SessionLoadParams>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(SessionLoadResponse.Completed);
            await fixture.ViewModel.ReplaceChatServiceAsync(backgroundChatService.Object);
            await DispatchConnectedAsync(fixture, "profile-1");
            await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));

            backgroundChatService.Raise(
                service => service.SessionUpdateReceived += null,
                new SessionUpdateEventArgs("remote-background", new AgentMessageUpdate(new TextContentBlock("background update"))));

            await WaitForConditionAsync(async () =>
            {
                var attentionState = await fixture.GetAttentionStateAsync();
                return HasUnreadAttention(attentionState, "conv-background");
            });

            var switched = await fixture.ViewModel.SwitchConversationAsync("conv-background");
            Assert.True(switched, fixture.ViewModel.ErrorMessage);
            await WaitForConditionAsync(async () =>
            {
                var state = await fixture.GetStateAsync();
                return string.Equals(state.HydratedConversationId, "conv-background", StringComparison.Ordinal);
            });

            await WaitForConditionAsync(async () =>
            {
                var attentionState = await fixture.GetAttentionStateAsync();
                return !HasUnreadAttention(attentionState, "conv-background");
            });

            var finalAttentionState = await fixture.GetAttentionStateAsync();
            Assert.True(finalAttentionState.TryGetConversation("conv-background", out var slice));
            Assert.NotNull(slice);
            Assert.False(slice!.HasUnread);
        });
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_SessionInfoUpdate_DoesNotMarkUnreadAttention()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await RunWithSynchronizationContextAsync(syncContext, async () =>
        {
            await using var fixture = CreateViewModel(syncContext);
            var chatService = CreateConnectedChatService();
            await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-foreground",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-foreground", new ConversationBindingSlice("conv-foreground", "remote-foreground", "profile-1"))
                    .Add("conv-background", new ConversationBindingSlice("conv-background", "remote-background", "profile-1"))
            });

            chatService.Raise(
                service => service.SessionUpdateReceived += null,
                new SessionUpdateEventArgs("remote-background", new SessionInfoUpdate
                {
                    Title = "Background title",
                    UpdatedAt = "2026-04-21T00:00:00Z"
                }));

            await Task.Delay(50);

            var attentionState = await fixture.GetAttentionStateAsync();
            Assert.False(HasUnreadAttention(attentionState, "conv-background"));
            Assert.False(HasUnreadAttention(attentionState, "conv-foreground"));
        });
    }

    [Fact]
    public async Task ReplaceChatServiceAsync_OldProfileServiceStopsProjectingUpdatesAfterProfileSwitch()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await RunWithSynchronizationContextAsync(syncContext, async () =>
        {
            await using var fixture = CreateViewModel(syncContext);
            var oldProfileService = CreateConnectedChatService();
            var activeProfileService = CreateConnectedChatService();

            await fixture.ViewModel.ReplaceChatServiceAsync(oldProfileService.Object);
            await fixture.ViewModel.ReplaceChatServiceAsync(activeProfileService.Object);

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-foreground",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-foreground", new ConversationBindingSlice("conv-foreground", "remote-foreground", "profile-2"))
                    .Add("conv-stale-profile-background", new ConversationBindingSlice("conv-stale-profile-background", "remote-stale-background", "profile-1"))
                    .Add("conv-active-profile-background", new ConversationBindingSlice("conv-active-profile-background", "remote-active-background", "profile-2"))
            });
            await WaitForConditionAsync(() =>
                Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-foreground", StringComparison.Ordinal)));
            await fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-2"));

            oldProfileService.Raise(
                service => service.SessionUpdateReceived += null,
                new SessionUpdateEventArgs("remote-stale-background", new AgentMessageUpdate(new TextContentBlock("stale profile update"))));

            var afterStaleUpdate = await fixture.GetAttentionStateAsync();
            Assert.False(HasUnreadAttention(afterStaleUpdate, "conv-stale-profile-background"));
            Assert.DoesNotContain(
                fixture.ViewModel.MessageHistory,
                message => string.Equals(message.TextContent, "stale profile update", StringComparison.Ordinal));

            activeProfileService.Raise(
                service => service.SessionUpdateReceived += null,
                new SessionUpdateEventArgs("remote-active-background", new AgentMessageUpdate(new TextContentBlock("active profile update"))));

            await WaitForConditionAsync(async () =>
            {
                var attentionState = await fixture.GetAttentionStateAsync();
                return HasUnreadAttention(attentionState, "conv-active-profile-background");
            });

            var finalAttentionState = await fixture.GetAttentionStateAsync();
            Assert.True(HasUnreadAttention(finalAttentionState, "conv-active-profile-background"));
            Assert.False(HasUnreadAttention(finalAttentionState, "conv-stale-profile-background"));
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
        await WaitForConditionAsync(() =>
            Task.FromResult(fixture.ViewModel.BottomPanelTabs.Count == 2));

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
        await WaitForConditionAsync(() =>
            Task.FromResult(fixture.ViewModel.BottomPanelTabs.Count == 2));

        Assert.Equal(2, fixture.ViewModel.BottomPanelTabs.Count);
        var outputTab = fixture.ViewModel.BottomPanelTabs[1];
        Assert.Equal("output", outputTab.Id);

        fixture.ViewModel.SelectedBottomPanelTab = outputTab;
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(
                fixture.ViewModel.SelectedBottomPanelTab?.Id,
                "output",
                StringComparison.Ordinal)));

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-2" });
        await Task.Delay(50);

        Assert.Equal("terminal", fixture.ViewModel.SelectedBottomPanelTab?.Id);

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await Task.Delay(50);

        Assert.Equal("output", fixture.ViewModel.SelectedBottomPanelTab?.Id);
    }

    [Fact]
    public async Task LocalTerminalPanel_LocalConversation_UsesConversationCwd()
    {
        var sessionManager = CreateSessionManagerWithStore();
        var localTerminalManager = new RecordingLocalTerminalSessionManager();
        var coordinator = new LocalTerminalPanelCoordinator(
            localTerminalManager,
            new LocalTerminalCwdResolver(() => @"C:\Users\shang"),
            new ImmediateUiDispatcher());
        await using var fixture = CreateViewModel(
            sessionManager: sessionManager,
            localTerminalPanelCoordinator: coordinator);

        await sessionManager.Object.CreateSessionAsync("conversation-local", @"C:\repo\local");
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conversation-local"
        });

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.ActiveLocalTerminalSession?.ConversationId == "conversation-local"));

        Assert.Equal(@"C:\repo\local", localTerminalManager.LastRequestedCwd);
        Assert.Equal(@"C:\repo\local", fixture.ViewModel.ActiveLocalTerminalSession?.CurrentWorkingDirectory);
    }

    [Fact]
    public async Task LocalTerminalPanel_RemoteBoundConversation_UsesUserHome()
    {
        var sessionManager = CreateSessionManagerWithStore();
        var localTerminalManager = new RecordingLocalTerminalSessionManager();
        var coordinator = new LocalTerminalPanelCoordinator(
            localTerminalManager,
            new LocalTerminalCwdResolver(() => @"C:\Users\shang"),
            new ImmediateUiDispatcher());
        await using var fixture = CreateViewModel(
            sessionManager: sessionManager,
            localTerminalPanelCoordinator: coordinator);

        await sessionManager.Object.CreateSessionAsync("conversation-remote", @"Z:\remote\repo");
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conversation-remote",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "conversation-remote",
                new ConversationBindingSlice("conversation-remote", "remote-session-1", "profile-1"))
        });

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.ActiveLocalTerminalSession?.ConversationId == "conversation-remote"));

        Assert.Equal(@"C:\Users\shang", localTerminalManager.LastRequestedCwd);
        Assert.Equal(@"C:\Users\shang", fixture.ViewModel.ActiveLocalTerminalSession?.CurrentWorkingDirectory);
    }

    [Fact]
    public async Task TerminalRequestReceived_Create_PreservesPublicBottomPanelSelectionWhileAddingSession()
    {
        await using var fixture = CreateViewModel();
        var chatService = CreateConnectedChatService();
        await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conversation-terminal-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "conversation-terminal-1",
                new ConversationBindingSlice("conversation-terminal-1", "remote-session-1", "profile-1"))
        });
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.BottomPanelTabs.Count == 2));

        var outputTab = fixture.ViewModel.BottomPanelTabs[1];
        Assert.Equal("output", outputTab.Id);
        fixture.ViewModel.SelectedBottomPanelTab = outputTab;
        await WaitForConditionAsync(() => Task.FromResult(
            string.Equals(fixture.ViewModel.SelectedBottomPanelTab?.Id, "output", StringComparison.Ordinal)));

        chatService.Raise(
            service => service.TerminalRequestReceived += null,
            chatService.Object,
            new TerminalRequestEventArgs(
                "request-1",
                "remote-session-1",
                "terminal-1",
                "terminal/create",
                ParseJsonParams("""{"terminalId":"terminal-1","command":"dotnet"}"""),
                _ => Task.FromResult(false)));

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.TerminalSessions.Count == 1
            && fixture.ViewModel.SelectedTerminalSession?.TerminalId == "terminal-1"));

        var terminal = Assert.Single(fixture.ViewModel.TerminalSessions);
        Assert.Equal("terminal-1", terminal.TerminalId);
        Assert.Equal("terminal/create", terminal.LastMethod);
        Assert.Equal(string.Empty, terminal.Output);
        Assert.Equal("output", fixture.ViewModel.SelectedBottomPanelTab?.Id);
    }

    [Fact]
    public async Task TerminalRequestReceived_SyntheticOutputPayload_UpdatesExistingSessionOutput()
    {
        await using var fixture = CreateViewModel();
        var chatService = CreateConnectedChatService();
        await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conversation-terminal-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "conversation-terminal-1",
                new ConversationBindingSlice("conversation-terminal-1", "remote-session-1", "profile-1"))
        });
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.BottomPanelTabs.Count == 2));

        chatService.Raise(
            service => service.TerminalRequestReceived += null,
            chatService.Object,
            new TerminalRequestEventArgs(
                "request-1",
                "remote-session-1",
                "terminal-1",
                "terminal/create",
                ParseJsonParams("""{"terminalId":"terminal-1"}"""),
                _ => Task.FromResult(false)));

        chatService.Raise(
            service => service.TerminalRequestReceived += null,
            chatService.Object,
            new TerminalRequestEventArgs(
                "request-2",
                "remote-session-1",
                "terminal-1",
                "terminal/output",
                ParseJsonParams("""{"terminalId":"terminal-1","output":"hello\n","truncated":false}"""),
                _ => Task.FromResult(false)));

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.SelectedTerminalSession?.Output.Contains("hello", StringComparison.Ordinal) == true));

        Assert.False(fixture.ViewModel.SelectedTerminalSession!.IsTruncated);
    }

    [Fact]
    public async Task TerminalStateChangedReceived_ProjectsLifecycleWithoutOwningPublicBottomPanelSelection()
    {
        await using var fixture = CreateViewModel();
        var chatService = CreateConnectedChatService();
        await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conversation-terminal-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "conversation-terminal-1",
                new ConversationBindingSlice("conversation-terminal-1", "remote-session-1", "profile-1"))
        });
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.BottomPanelTabs.Count == 2));

        var outputTab = fixture.ViewModel.BottomPanelTabs[1];
        Assert.Equal("output", outputTab.Id);
        fixture.ViewModel.SelectedBottomPanelTab = outputTab;
        await WaitForConditionAsync(() => Task.FromResult(
            string.Equals(fixture.ViewModel.SelectedBottomPanelTab?.Id, "output", StringComparison.Ordinal)));

        chatService.Raise(
            service => service.TerminalStateChangedReceived += null,
            chatService.Object,
            new TerminalStateChangedEventArgs(
                "remote-session-1",
                "terminal-1",
                "terminal/create"));

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.SelectedTerminalSession?.TerminalId == "terminal-1"));

        chatService.Raise(
            service => service.TerminalStateChangedReceived += null,
            chatService.Object,
            new TerminalStateChangedEventArgs(
                "remote-session-1",
                "terminal-1",
                "terminal/output",
                "hello\n",
                false,
                new TerminalExitStatus { ExitCode = 0 }));

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.SelectedTerminalSession?.Output.Contains("hello", StringComparison.Ordinal) == true
            && fixture.ViewModel.SelectedTerminalSession.ExitCode == 0));

        chatService.Raise(
            service => service.TerminalStateChangedReceived += null,
            chatService.Object,
            new TerminalStateChangedEventArgs(
                "remote-session-1",
                "terminal-1",
                "terminal/kill"));

        await WaitForConditionAsync(() => Task.FromResult(
            string.Equals(
                fixture.ViewModel.SelectedTerminalSession?.LastMethod,
                "terminal/kill",
                StringComparison.Ordinal)));

        chatService.Raise(
            service => service.TerminalStateChangedReceived += null,
            chatService.Object,
            new TerminalStateChangedEventArgs(
                "remote-session-1",
                "terminal-1",
                "terminal/release",
                isReleased: true));

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.SelectedTerminalSession?.IsReleased == true));

        Assert.False(fixture.ViewModel.SelectedTerminalSession!.IsTruncated);
        Assert.Equal("output", fixture.ViewModel.SelectedBottomPanelTab?.Id);
    }

    [Fact]
    public async Task TerminalRequestReceived_ForBackgroundRemoteSession_DoesNotAttachToActiveConversation()
    {
        await using var fixture = CreateViewModel();
        var chatService = CreateConnectedChatService();
        await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conversation-2",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conversation-1", new ConversationBindingSlice("conversation-1", "remote-session-1", "profile-1"))
                .Add("conversation-2", new ConversationBindingSlice("conversation-2", "remote-session-2", "profile-1"))
        });
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.BottomPanelTabs.Count == 2));

        chatService.Raise(
            service => service.TerminalRequestReceived += null,
            chatService.Object,
            new TerminalRequestEventArgs(
                "request-1",
                "remote-session-1",
                "terminal-1",
                "terminal/create",
                ParseJsonParams("""{"terminalId":"terminal-1"}"""),
                _ => Task.FromResult(false)));

        await Task.Delay(100);
        Assert.Empty(fixture.ViewModel.TerminalSessions);
        Assert.Null(fixture.ViewModel.SelectedTerminalSession);
        Assert.Equal("terminal", fixture.ViewModel.SelectedBottomPanelTab?.Id);

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conversation-1" });
        await WaitForConditionAsync(() => Task.FromResult(
            string.Equals(fixture.ViewModel.CurrentSessionId, "conversation-1", StringComparison.Ordinal)));
        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.TerminalSessions.Count == 1
            && fixture.ViewModel.SelectedTerminalSession?.TerminalId == "terminal-1"));
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
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.BottomPanelTabs.Count >= 2));

        var outputTab = fixture.ViewModel.BottomPanelTabs[1];
        Assert.Equal("output", outputTab.Id);
        fixture.ViewModel.SelectedBottomPanelTab = outputTab;
        await WaitForConditionAsync(() => Task.FromResult(
            string.Equals(fixture.ViewModel.SelectedBottomPanelTab?.Id, "output", StringComparison.Ordinal)));

        var archiveResult = await fixture.ViewModel.ArchiveConversationAsync("session-1");
        Assert.True(archiveResult.Succeeded, archiveResult.FailureReason);

        activation.Verify(a => a.ArchiveConversationAsync("session-1", "session-1", It.IsAny<CancellationToken>()), Times.Once);

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.BottomPanelTabs.Count == 0
            && fixture.ViewModel.SelectedBottomPanelTab is null));

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


        var outputTab = fixture.ViewModel.BottomPanelTabs[1];
        Assert.Equal("output", outputTab.Id);
        fixture.ViewModel.SelectedBottomPanelTab = outputTab;
        await Task.Delay(50);
        Assert.Equal("output", fixture.ViewModel.SelectedBottomPanelTab?.Id);

        var deleteResult = await fixture.ViewModel.DeleteConversationAsync("session-1");
        Assert.True(deleteResult.Succeeded, deleteResult.FailureReason);

        activation.Verify(a => a.DeleteConversationAsync("session-1", "session-1", It.IsAny<CancellationToken>()), Times.Once);

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.BottomPanelTabs.Count == 0
            && fixture.ViewModel.SelectedBottomPanelTab is null));

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
    public async Task ArchiveConversation_WhenCoordinatorReturnsFailedResult_PreservesBottomPanelState()
    {
        var activation = new Mock<IConversationActivationCoordinator>();
        activation
            .Setup(a => a.ArchiveConversationAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationMutationResult(false, false, "ArchiveFailed"));

        await using var fixture = CreateViewModel(conversationActivationCoordinator: activation.Object);

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.BottomPanelTabs.Count >= 2));

        var outputTab = fixture.ViewModel.BottomPanelTabs[1];
        fixture.ViewModel.SelectedBottomPanelTab = outputTab;
        await WaitForConditionAsync(() => Task.FromResult(
            string.Equals(fixture.ViewModel.SelectedBottomPanelTab?.Id, "output", StringComparison.Ordinal)));

        await fixture.ViewModel.ArchiveConversationAsync("session-1");

        activation.Verify(a => a.ArchiveConversationAsync("session-1", "session-1", It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotEmpty(fixture.ViewModel.BottomPanelTabs);
        Assert.Equal("output", fixture.ViewModel.SelectedBottomPanelTab?.Id);
    }

    [Fact]
    public async Task ArchiveConversation_WhenCoordinatorIsSlow_DoesNotBlockCallerThread()
    {
        var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activation = new Mock<IConversationActivationCoordinator>();
        activation
            .Setup(a => a.ArchiveConversationAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                started.TrySetResult(null);
                await allowCompletion.Task;
                return new ConversationMutationResult(true, true, null);
            });

        await using var fixture = CreateViewModel(conversationActivationCoordinator: activation.Object);
        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        await Task.Delay(50);

        var archiveTask = fixture.ViewModel.ArchiveConversationAsync("session-1");
        var completed = await Task.WhenAny(archiveTask, Task.Delay(200));
        Assert.True(completed != archiveTask, "ArchiveConversationAsync should stay pending while backend mutation is still running.");

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        allowCompletion.TrySetResult(null);
        var result = await archiveTask;
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ArchiveConversation_WhenMutationCompletesOffUi_UpdatesBottomPanelStateOnUiContext()
    {
        var mutationCompleted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activation = new Mock<IConversationActivationCoordinator>();
        activation
            .Setup(a => a.ArchiveConversationAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Yield();
                mutationCompleted.TrySetResult(null);
                return new ConversationMutationResult(true, true, null);
            });

        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext, conversationActivationCoordinator: activation.Object);
        syncContext.RunAll();

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "session-1" });
        syncContext.RunAll();
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.BottomPanelTabs.Count > 0));

        var archiveTask = fixture.ViewModel.ArchiveConversationAsync("session-1");

        await mutationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotEmpty(fixture.ViewModel.BottomPanelTabs);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                fixture.ViewModel.BottomPanelTabs.Count == 0
                && fixture.ViewModel.SelectedBottomPanelTab is null);
        });
        await archiveTask;
    }

    [Fact]
    public async Task ArchiveConversation_AfterRemoteSessionInfoUpdate_DoesNotResurrectArchivedConversation()
    {
        var chatService = CreateConnectedChatService();
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        WireViewModelCoordinatorIntoFacade(fixture.ViewModel);

        await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

        await fixture.Workspace.RegisterConversationAsync("session-1");
        await fixture.DispatchAsync(new SetBindingSliceAction(new ConversationBindingSlice("session-1", "remote-1", "profile-1")));
        await fixture.DispatchAsync(new SelectConversationAction("session-1"));

        await fixture.ViewModel.ArchiveConversationAsync("session-1");

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            return !fixture.Workspace.GetKnownConversationIds().Contains("session-1", StringComparer.Ordinal)
                && fixture.Workspace.GetConversationSnapshot("session-1") is null
                && state.ResolveBinding("session-1") is null;
        }, timeoutMilliseconds: 10000);

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-1",
                new SessionInfoUpdate
                {
                    Title = "zombie",
                    UpdatedAt = "2026-04-05T14:00:00Z"
                }));

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            return !fixture.Workspace.GetKnownConversationIds().Contains("session-1", StringComparer.Ordinal)
                && fixture.Workspace.GetConversationSnapshot("session-1") is null
                && state.ResolveBinding("session-1") is null;
        }, timeoutMilliseconds: 10000);
    }

    [Fact]
    public async Task DeleteConversation_AfterRemoteSessionInfoUpdate_DoesNotResurrectDeletedConversation()
    {
        var chatService = CreateConnectedChatService();
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        WireViewModelCoordinatorIntoFacade(fixture.ViewModel);

        await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

        await fixture.Workspace.RegisterConversationAsync("session-1");
        await fixture.DispatchAsync(new SetBindingSliceAction(new ConversationBindingSlice("session-1", "remote-1", "profile-1")));
        await fixture.DispatchAsync(new SelectConversationAction("session-1"));

        await fixture.ViewModel.DeleteConversationAsync("session-1");

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            return !fixture.Workspace.GetKnownConversationIds().Contains("session-1", StringComparer.Ordinal)
                && state.ResolveBinding("session-1") is null;
        });

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-1",
                new SessionInfoUpdate
                {
                    Title = "zombie-delete",
                    UpdatedAt = "2026-04-05T14:00:00Z"
                }));

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            return !fixture.Workspace.GetKnownConversationIds().Contains("session-1", StringComparer.Ordinal)
                && fixture.Workspace.GetConversationSnapshot("session-1") is null
                && state.ResolveBinding("session-1") is null;
        });
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
        var activation = new Mock<IConversationActivationCoordinator>();
        activation
            .Setup(a => a.ArchiveConversationAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationMutationResult(true, true, null));

        await using var fixture = CreateViewModel(conversationActivationCoordinator: activation.Object);
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

        await fixture.ViewModel.ArchiveConversationAsync("session-1");
        await Task.Delay(50);

        var state = await fixture.GetStateAsync();
        Assert.Null(fixture.ViewModel.CurrentSessionId);
        Assert.False(fixture.ViewModel.IsSessionActive);
        Assert.Null(state.HydratedConversationId);
    }

    [Fact]
    public async Task DeleteConversation_CurrentSession_ClearsStoreSelection()
    {
        var activation = new Mock<IConversationActivationCoordinator>();
        activation
            .Setup(a => a.DeleteConversationAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationMutationResult(true, true, null));

        await using var fixture = CreateViewModel(conversationActivationCoordinator: activation.Object);
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

        await fixture.ViewModel.DeleteConversationAsync("session-1");
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
        Assert.Equal("profile-a", connectionState.SettingsSelectedProfileId);
    }

    [Fact]
    public async Task SelectProfile_WhenProfileIsNotLoaded_PreservesStoreSelectionWithoutProjectingDetachedInstance()
    {
        await using var fixture = CreateViewModel();
        var detachedProfile = new ServerConfiguration { Id = "profile-a", Name = "Profile A", Transport = TransportType.Stdio };

        fixture.ViewModel.SelectProfile(detachedProfile);
        await Task.Delay(50);

        var connectionState = await fixture.GetConnectionStateAsync();
        Assert.Equal("profile-a", connectionState.SettingsSelectedProfileId);
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
        await fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-a"));

        Assert.Equal("Configured Agent", fixture.ViewModel.CurrentAgentDisplayText);

        await fixture.UpdateStateAsync(state => state with
        {
            AgentProfileId = "profile-a",
            AgentName = "Protocol Agent"
        });
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.CurrentAgentDisplayText, "Protocol Agent", StringComparison.Ordinal)));

        Assert.Equal("Protocol Agent", fixture.ViewModel.CurrentAgentDisplayText);
    }

    [Fact]
    public async Task CurrentAgentDisplayText_WhenActiveSessionChanges_DoesNotLeakPreviousSessionAgentIdentity()
    {
        await using var fixture = CreateViewModel();

        var profile1 = new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio };
        var profile2 = new ServerConfiguration { Id = "profile-2", Name = "Profile 2", Transport = TransportType.Stdio };
        fixture.Profiles.Profiles.Add(profile1);
        fixture.Profiles.Profiles.Add(profile2);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-2",
            AgentName = "stale-agent",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-2"))
        });
        await fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-2"));

        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.SelectedAcpProfile?.Id, "profile-2", StringComparison.Ordinal)));

        Assert.Equal("Profile 2", fixture.ViewModel.CurrentAgentDisplayText);
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
    public async Task SessionUpdate_AfterBindingChange_UsesStoreBindingInsteadOfStaleProjectedRemoteSessionId()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-old", "profile-a")),
            HydratedConversationId = "session-1"
        });
        await WaitForConditionAsync(() => Task.FromResult(
            string.Equals(fixture.ViewModel.CurrentSessionId, "session-1", StringComparison.Ordinal)
            && string.Equals(fixture.ViewModel.CurrentRemoteSessionId, "remote-old", StringComparison.Ordinal)));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-new", "profile-a"))
        });
        await WaitForConditionAsync(() => Task.FromResult(
            string.Equals(fixture.ViewModel.CurrentRemoteSessionId, "remote-new", StringComparison.Ordinal)));

        chatService.Raise(
            x => x.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-new",
                new AgentMessageUpdate(new TextContentBlock { Text = "fresh reply" })));

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.MessageHistory.Count == 1
            && string.Equals(fixture.ViewModel.MessageHistory[0].TextContent, "fresh reply", StringComparison.Ordinal)));

        var message = Assert.Single(fixture.ViewModel.MessageHistory);
        Assert.Equal("fresh reply", message.TextContent);
    }

    [Fact]
    public async Task SessionUpdate_UnrelatedRemoteSession_IsIgnored_WhenStoreBindingTargetsDifferentSession()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-good", "profile-a"))
        });
        await Task.Delay(50);

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
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-fresh", "profile-a"))
        });
        await WaitForConditionAsync(() => Task.FromResult(
            string.Equals(fixture.ViewModel.CurrentSessionId, "session-1", StringComparison.Ordinal)
            && string.Equals(fixture.ViewModel.CurrentRemoteSessionId, "remote-fresh", StringComparison.Ordinal)));

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
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-fresh", "profile-a"))
        });
        await Task.Delay(50);

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
        fixture.Preferences.LastSelectedServerId = "profile-1";

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

        fixture.Preferences.LastSelectedServerId = profile.Id;

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
        fixture.Preferences.LastSelectedServerId = "profile-1";

        await fixture.ViewModel.TryAutoConnectAsync(CancellationToken.None);

        Assert.Null(fixture.ViewModel.SelectedAcpProfile);
        Assert.Null(fixture.Profiles.SelectedProfile);
        commands.Verify(
            x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
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
    public async Task StoreTranscript_WithStructuredToolCallContent_ProjectsToMessageHistoryWithoutLoss()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        syncContext.RunAll();

        var snapshot = new ConversationMessageSnapshot
        {
            Id = "tool-1",
            ContentType = "tool_call",
            ToolCallId = "call-1",
            ToolCallStatus = ToolCallStatus.InProgress,
            ToolCallContent = new List<ToolCallContent>
            {
                new ContentToolCallContent(new ImageContentBlock("image-data", "image/png"))
            },
            Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Transcript = ImmutableList<ConversationMessageSnapshot>.Empty.Add(snapshot)
        });

        await Task.Delay(50);
        syncContext.RunAll();

        var projected = Assert.Single(fixture.ViewModel.MessageHistory);
        var structuredContent = Assert.Single(projected.ToolCallContent!);
        var content = Assert.IsType<ContentToolCallContent>(structuredContent);
        var image = Assert.IsType<ImageContentBlock>(content.Content);
        Assert.Equal("image-data", image.Data);
        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_ToolCallUpdate_WithImageContent_PreservesStructuredContent()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "conv-1",
                new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new ToolCallUpdate
            {
                ToolCallId = "tool-image",
                Status = ToolCallStatus.InProgress,
                Title = "image tool",
                Content = new List<ToolCallContent>
                {
                    new ContentToolCallContent(new ImageContentBlock("image-data", "image/png"))
                }
            }));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var state = await fixture.GetStateAsync();
            var transcript = state.ConversationContents?.TryGetValue("conv-1", out var content) == true
                ? content.Transcript
                : ImmutableList<ConversationMessageSnapshot>.Empty;
            return transcript.Any(message => string.Equals(message.ToolCallId, "tool-image", StringComparison.Ordinal));
        });

        var finalState = await fixture.GetStateAsync();
        var snapshot = Assert.Single(finalState.ConversationContents!["conv-1"].Transcript);
        Assert.True(string.IsNullOrWhiteSpace(snapshot.TextContent));

        var structuredContent = GetStructuredToolCallContent(snapshot);
        var toolCallContent = Assert.Single(structuredContent);
        var content = Assert.IsType<ContentToolCallContent>(toolCallContent);
        var image = Assert.IsType<ImageContentBlock>(content.Content);
        Assert.Equal("image-data", image.Data);
        Assert.Equal("image/png", image.MimeType);
        Assert.Contains("\"type\":\"content\"", snapshot.ToolCallJson, StringComparison.Ordinal);
        Assert.Contains("\"mimeType\":\"image/png\"", snapshot.ToolCallJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_ToolCallUpdate_WithResourceLinkContent_PreservesStructuredContent()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "conv-1",
                new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new ToolCallUpdate
            {
                ToolCallId = "tool-resource-link",
                Status = ToolCallStatus.InProgress,
                Title = "resource link tool",
                Content = new List<ToolCallContent>
                {
                    new ContentToolCallContent(new ResourceLinkContentBlock("https://example.com/doc"))
                }
            }));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var state = await fixture.GetStateAsync();
            var transcript = state.ConversationContents?.TryGetValue("conv-1", out var content) == true
                ? content.Transcript
                : ImmutableList<ConversationMessageSnapshot>.Empty;
            return transcript.Any(message => string.Equals(message.ToolCallId, "tool-resource-link", StringComparison.Ordinal));
        });

        var finalState = await fixture.GetStateAsync();
        var snapshot = Assert.Single(finalState.ConversationContents!["conv-1"].Transcript);
        Assert.True(string.IsNullOrWhiteSpace(snapshot.TextContent));

        var structuredContent = GetStructuredToolCallContent(snapshot);
        var toolCallContent = Assert.Single(structuredContent);
        var content = Assert.IsType<ContentToolCallContent>(toolCallContent);
        var resourceLink = Assert.IsType<ResourceLinkContentBlock>(content.Content);
        Assert.Equal("https://example.com/doc", resourceLink.Uri);
        Assert.Contains("\"type\":\"content\"", snapshot.ToolCallJson, StringComparison.Ordinal);
        Assert.Contains("https://example.com/doc", snapshot.ToolCallJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_ToolCallUpdate_WithMixedContent_PreservesStructuredContentAndTextFallback()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "conv-1",
                new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new ToolCallUpdate
            {
                ToolCallId = "tool-mixed",
                Status = ToolCallStatus.InProgress,
                Title = "mixed tool",
                Content = new List<ToolCallContent>
                {
                    new ContentToolCallContent(new TextContentBlock("alpha")),
                    new ContentToolCallContent(new ImageContentBlock("image-data", "image/png")),
                    new ContentToolCallContent(new ResourceLinkContentBlock("https://example.com/doc"))
                }
            }));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var state = await fixture.GetStateAsync();
            var transcript = state.ConversationContents?.TryGetValue("conv-1", out var content) == true
                ? content.Transcript
                : ImmutableList<ConversationMessageSnapshot>.Empty;
            return transcript.Any(message => string.Equals(message.ToolCallId, "tool-mixed", StringComparison.Ordinal));
        });

        var finalState = await fixture.GetStateAsync();
        var snapshot = Assert.Single(finalState.ConversationContents!["conv-1"].Transcript);
        Assert.Equal("alpha", snapshot.TextContent);

        var structuredContent = GetStructuredToolCallContent(snapshot);
        Assert.Equal(3, structuredContent.Count);
        Assert.IsType<TextContentBlock>(Assert.IsType<ContentToolCallContent>(structuredContent[0]).Content);
        Assert.IsType<ImageContentBlock>(Assert.IsType<ContentToolCallContent>(structuredContent[1]).Content);
        Assert.IsType<ResourceLinkContentBlock>(Assert.IsType<ContentToolCallContent>(structuredContent[2]).Content);
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
        var uiDispatcher = (IUiDispatcher)syncContext;

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object,
            uiDispatcher);

        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object, new ImmediateUiDispatcher());
        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument());
        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var workspace = new ChatConversationWorkspace(
            sessionManager.Object,
            conversationStore.Object,
            new AppPreferencesConversationWorkspacePreferences(preferences),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            uiDispatcher);
        var conversationCatalogPresenter = new ConversationCatalogPresenter();
        var conversationCatalogFacade = new ConversationCatalogFacade(
            workspace,
            new NavigationProjectPreferencesAdapter(preferences),
            Mock.Of<IConversationActivationCoordinator>(),
            Mock.Of<IShellSelectionReadModel>(),
            new Lazy<INavigationCoordinator>(() => Mock.Of<INavigationCoordinator>()),
            conversationCatalogPresenter,
            NullLogger<ConversationCatalogFacade>.Instance);
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
            uiDispatcher,
            Mock.Of<IConversationPreviewStore>(),
            vmLogger.Object,
            conversationCatalogFacade: conversationCatalogFacade);

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
        var uiDispatcher = (IUiDispatcher)syncContext;

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object,
            uiDispatcher);

        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object, new ImmediateUiDispatcher());
        var conversationStore = new Mock<IConversationStore>();
        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var workspace = new ChatConversationWorkspace(
            sessionManager.Object,
            conversationStore.Object,
            new AppPreferencesConversationWorkspacePreferences(preferences),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            uiDispatcher);
        var conversationCatalogPresenter = new ConversationCatalogPresenter();
        var conversationCatalogFacade = new ConversationCatalogFacade(
            workspace,
            new NavigationProjectPreferencesAdapter(preferences),
            Mock.Of<IConversationActivationCoordinator>(),
            Mock.Of<IShellSelectionReadModel>(),
            new Lazy<INavigationCoordinator>(() => Mock.Of<INavigationCoordinator>()),
            conversationCatalogPresenter,
            NullLogger<ConversationCatalogFacade>.Instance);
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
            uiDispatcher,
            Mock.Of<IConversationPreviewStore>(),
            vmLogger.Object,
            conversationCatalogFacade: conversationCatalogFacade);

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
        var syncContext = new ImmediateSynchronizationContext();
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
        var uiDispatcher = (IUiDispatcher)syncContext;

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object,
            uiDispatcher);

        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object, new ImmediateUiDispatcher());
        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument());
        var workspace = new ChatConversationWorkspace(
            sessionManager.Object,
            conversationStore.Object,
            new AppPreferencesConversationWorkspacePreferences(preferences),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            uiDispatcher);
        var miniWindow = new Mock<IMiniWindowCoordinator>();
        var conversationCatalogPresenter = new ConversationCatalogPresenter();
        var conversationCatalogFacade = new ConversationCatalogFacade(
            workspace,
            new NavigationProjectPreferencesAdapter(preferences),
            Mock.Of<IConversationActivationCoordinator>(),
            Mock.Of<IShellSelectionReadModel>(),
            new Lazy<INavigationCoordinator>(() => Mock.Of<INavigationCoordinator>()),
            conversationCatalogPresenter,
            NullLogger<ConversationCatalogFacade>.Instance);
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
                uiDispatcher,
                Mock.Of<IConversationPreviewStore>(),
                vmLogger.Object,
                conversationCatalogFacade: conversationCatalogFacade);

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
    public async Task ConnectionInstanceId_ProjectsFromStoreAndRaisesPropertyChangedOnce()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        var notifications = 0;
        fixture.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatViewModel.ConnectionInstanceId))
            {
                notifications++;
            }
        };

        Assert.Null(fixture.ViewModel.ConnectionInstanceId);

        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        syncContext.RunAll();

        Assert.Equal("conn-1", fixture.ViewModel.ConnectionInstanceId);
        Assert.Equal(1, notifications);

        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        syncContext.RunAll();

        Assert.Equal("conn-1", fixture.ViewModel.ConnectionInstanceId);
        Assert.Equal(1, notifications);
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

    private sealed class QueueingSynchronizationContext : SynchronizationContext, IUiDispatcher
    {
        private readonly Queue<(SendOrPostCallback callback, object? state)> _work = new();
        private readonly object _gate = new();

        public bool HasThreadAccess => ReferenceEquals(Current, this);

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

        public void Enqueue(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            Post(_ => action(), null);
        }

        public Task EnqueueAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        public Task EnqueueAsync(Func<Task> function)
        {
            ArgumentNullException.ThrowIfNull(function);

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try
                {
                    await function().ConfigureAwait(false);
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);

            return tcs.Task;
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
            RunAll();
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext, IUiDispatcher
    {
        public bool HasThreadAccess => true;

        public override void Post(SendOrPostCallback d, object? state) => d(state);

        public void Enqueue(Action action) => action();

        public Task EnqueueAsync(Action action)
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        public async Task EnqueueAsync(Func<Task> function)
        {
            await function().ConfigureAwait(false);
        }
    }

    private sealed class FakeNavigationPaneState : INavigationPaneState
    {
        public bool IsPaneOpen { get; private set; }

        public event EventHandler? PaneStateChanged;

        public void SetPaneOpen(bool isOpen)
        {
            if (IsPaneOpen == isOpen)
            {
                return;
            }

            IsPaneOpen = isOpen;
            PaneStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static async Task DispatchConnectedAsync(ViewModelFixture fixture, string profileId)
    {
        await fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction(profileId));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
    }

    private static Task AwaitWithSynchronizationContextAsync(SynchronizationContext syncContext, Task task)
        => syncContext is QueueingSynchronizationContext queueingContext
            ? queueingContext.RunUntilCompletedAsync(task)
            : task;

    private static async Task RunWithSynchronizationContextAsync(SynchronizationContext syncContext, Func<Task> action)
    {
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(syncContext);
            await action().ConfigureAwait(false);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

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
        int timeoutMilliseconds = 8000,
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

    private static JsonElement ParseJsonParams(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static Mock<IChatService> CreateConnectedChatService()
    {
        var chatService = new Mock<IChatService>();
        chatService.SetupGet(service => service.IsConnected).Returns(true);
        chatService.SetupGet(service => service.IsInitialized).Returns(true);
        chatService.SetupGet(service => service.SessionHistory).Returns(Array.Empty<SessionUpdateEntry>());
        return chatService;
    }

    private static IReadOnlyList<ToolCallContent> GetStructuredToolCallContent(ConversationMessageSnapshot snapshot)
    {
        Assert.NotNull(snapshot.ToolCallContent);
        return snapshot.ToolCallContent!;
    }

    private static bool HasUnreadAttention(ConversationAttentionState attentionState, string conversationId)
        => attentionState.TryGetConversation(conversationId, out var slice) && slice is { HasUnread: true };

    private static ServerConfiguration CreateConnectableStdioProfile(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe",
            StdioArgs = "--serve"
        };

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

        public event EventHandler<TerminalStateChangedEventArgs>? TerminalStateChangedReceived
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

    private sealed class ContinuityTrackingChatService : IChatService
    {
        private readonly HashSet<string> _knownRemoteSessions = new(StringComparer.Ordinal);
        private int _nextRecoveredSessionId = 1;
        private event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceivedCore;

        public string? CurrentSessionId { get; private set; }
        public bool IsInitialized => true;
        public bool IsConnected => true;
        public AgentInfo? AgentInfo => new("agent", "1.0.0");
        public AgentCapabilities? AgentCapabilities => new(
            loadSession: true,
            sessionCapabilities: new SessionCapabilities
            {
                List = new SessionListCapabilities()
            });
        public IReadOnlyList<SessionUpdateEntry> SessionHistory => Array.Empty<SessionUpdateEntry>();
        public Plan? CurrentPlan => null;
        public SessionModeState? CurrentMode => null;
        public int LoadSessionCallCount { get; private set; }
        public int SendPromptCallCount { get; private set; }
        public int CreateSessionCallCount { get; private set; }
        public int ListSessionsCallCount { get; private set; }
        public List<string> LoadedSessionIds { get; } = [];
        public List<string> PromptSessionIds { get; } = [];
        public List<string?> SessionListCursors { get; } = [];
        public Func<SessionLoadParams, CancellationToken, Task<SessionLoadResponse>>? OnLoadSessionAsync { get; set; }
        public Func<SessionListParams?, CancellationToken, Task<SessionListResponse>>? OnListSessionsAsync { get; set; }

        public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived
        {
            add => SessionUpdateReceivedCore += value;
            remove => SessionUpdateReceivedCore -= value;
        }

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

        public event EventHandler<TerminalStateChangedEventArgs>? TerminalStateChangedReceived
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

        public Task<InitializeResponse> InitializeAsync(InitializeParams @params)
            => Task.FromResult(new InitializeResponse(
                1,
                new AgentInfo("agent", "1.0.0"),
                new AgentCapabilities(loadSession: true)));

        public Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params)
        {
            CreateSessionCallCount++;
            var sessionId = $"remote-recovered-{_nextRecoveredSessionId++}";
            _knownRemoteSessions.Add(sessionId);
            CurrentSessionId = sessionId;
            return Task.FromResult(new SessionNewResponse(sessionId));
        }

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params)
            => LoadSessionAsync(@params, CancellationToken.None);

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken)
        {
            LoadSessionCallCount++;
            CurrentSessionId = @params.SessionId;
            _knownRemoteSessions.Add(@params.SessionId);
            LoadedSessionIds.Add(@params.SessionId);
            return OnLoadSessionAsync?.Invoke(@params, cancellationToken)
                ?? Task.FromResult(SessionLoadResponse.Completed);
        }

        public Task<SessionListResponse> ListSessionsAsync(SessionListParams? @params = null, CancellationToken cancellationToken = default)
        {
            ListSessionsCallCount++;
            SessionListCursors.Add(@params?.Cursor);
            return OnListSessionsAsync?.Invoke(@params, cancellationToken)
                ?? Task.FromResult(new SessionListResponse());
        }

        public Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default)
        {
            SendPromptCallCount++;
            PromptSessionIds.Add(@params.SessionId);
            if (!_knownRemoteSessions.Contains(@params.SessionId))
            {
                throw new AcpException(JsonRpcErrorCode.SessionNotFound, $"Session '{@params.SessionId}' not found");
            }

            CurrentSessionId = @params.SessionId;
            return Task.FromResult(new SessionPromptResponse(StopReason.EndTurn, "user-message-1"));
        }

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
            => Task.FromResult(true);

        public Task<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?> GetAvailableModesAsync()
            => throw new NotSupportedException();

        public void ClearHistory()
        {
        }

        public void RaiseSessionUpdate(SessionUpdateEventArgs args)
            => SessionUpdateReceivedCore?.Invoke(this, args);
    }

    private sealed class ForwardingAcpConnectionCommands : IAcpConnectionCommands
    {
        public IAcpConnectionCommands? Inner { get; set; }

        public Task<AcpTransportApplyResult> ConnectToProfileAsync(
            ServerConfiguration profile,
            IAcpTransportConfiguration transportConfiguration,
            IAcpChatCoordinatorSink sink,
            CancellationToken cancellationToken = default)
            => RequireInner().ConnectToProfileAsync(profile, transportConfiguration, sink, cancellationToken);

        public Task<AcpTransportApplyResult> ConnectToProfileAsync(
            ServerConfiguration profile,
            IAcpTransportConfiguration transportConfiguration,
            IAcpChatCoordinatorSink sink,
            AcpConnectionContext connectionContext,
            CancellationToken cancellationToken = default)
            => RequireInner().ConnectToProfileAsync(profile, transportConfiguration, sink, connectionContext, cancellationToken);

        public Task<AcpTransportApplyResult> ApplyTransportConfigurationAsync(
            IAcpTransportConfiguration transportConfiguration,
            IAcpChatCoordinatorSink sink,
            bool preserveConversation,
            CancellationToken cancellationToken = default)
            => RequireInner().ApplyTransportConfigurationAsync(transportConfiguration, sink, preserveConversation, cancellationToken);

        public Task<AcpTransportApplyResult> ApplyTransportConfigurationAsync(
            IAcpTransportConfiguration transportConfiguration,
            IAcpChatCoordinatorSink sink,
            AcpConnectionContext connectionContext,
            CancellationToken cancellationToken = default)
            => RequireInner().ApplyTransportConfigurationAsync(transportConfiguration, sink, connectionContext, cancellationToken);

        public Task<AcpRemoteSessionResult> EnsureRemoteSessionAsync(
            IAcpChatCoordinatorSink sink,
            Func<CancellationToken, Task<bool>> authenticateAsync,
            CancellationToken cancellationToken = default)
            => RequireInner().EnsureRemoteSessionAsync(sink, authenticateAsync, cancellationToken);

        public Task<AcpPromptDispatchResult> SendPromptAsync(
            string promptText,
            string? promptMessageId,
            IAcpChatCoordinatorSink sink,
            Func<CancellationToken, Task<bool>> authenticateAsync,
            CancellationToken cancellationToken = default)
            => RequireInner().SendPromptAsync(promptText, promptMessageId, sink, authenticateAsync, cancellationToken);

        public Task<AcpPromptDispatchResult> DispatchPromptToRemoteSessionAsync(
            string remoteSessionId,
            string promptText,
            string? promptMessageId,
            IAcpChatCoordinatorSink sink,
            Func<CancellationToken, Task<bool>> authenticateAsync,
            CancellationToken cancellationToken = default)
            => RequireInner().DispatchPromptToRemoteSessionAsync(
                remoteSessionId,
                promptText,
                promptMessageId,
                sink,
                authenticateAsync,
                cancellationToken);

        public Task CancelPromptAsync(
            IAcpChatCoordinatorSink sink,
            string? reason = null,
            CancellationToken cancellationToken = default)
            => RequireInner().CancelPromptAsync(sink, reason, cancellationToken);

        public Task DisconnectAsync(
            IAcpChatCoordinatorSink sink,
            CancellationToken cancellationToken = default)
            => RequireInner().DisconnectAsync(sink, cancellationToken);

        public Task<AcpTransportApplyResult> ConnectProfileInPoolAsync(
            ServerConfiguration profile,
            IAcpTransportConfiguration transportConfiguration,
            CancellationToken cancellationToken = default)
            => RequireInner().ConnectProfileInPoolAsync(profile, transportConfiguration, cancellationToken);

        public Task DisconnectProfileInPoolAsync(
            string profileId,
            CancellationToken cancellationToken = default)
            => RequireInner().DisconnectProfileInPoolAsync(profileId, cancellationToken);

        private IAcpConnectionCommands RequireInner()
            => Inner ?? throw new InvalidOperationException("Connection commands have not been initialized.");
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

    private sealed class ViewModelFixture : IDisposable, IAsyncDisposable
    {
        private readonly IState<ChatState> _state;
        private readonly IState<ChatConnectionState> _connectionState;
        private readonly IState<ConversationAttentionState> _attentionState;
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
            IState<ConversationAttentionState> attentionState,
            RecordingChatStore chatStore,
            Mock<ILogger<ChatViewModel>> viewModelLogger)
        {
            ViewModel = viewModel;
            _state = state;
            _connectionState = connectionState;
            _attentionState = attentionState;
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

        public async Task<ConversationAttentionState> GetAttentionStateAsync() => await _attentionState ?? ConversationAttentionState.Empty;

        public ValueTask DispatchAsync(ChatAction action) => _store.Dispatch(action);

        public ValueTask DispatchConnectionAsync(ChatConnectionAction action) => _connectionStore.Dispatch(action);

        public ValueTask UpdateStateAsync(Func<ChatState, ChatState> update)
            => _chatStore.SetStateAsync(update(_chatStore.LatestState));

        public async ValueTask DisposeAsync()
        {
            ViewModel.Dispose();
            _workspace.Dispose();
            await _connectionState.DisposeAsync();
            await _attentionState.DisposeAsync();
            await _state.DisposeAsync();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class RecordingLocalTerminalSessionManager : ILocalTerminalSessionManager
    {
        private readonly Dictionary<string, RecordingLocalTerminalSession> _sessions = new(StringComparer.Ordinal);

        public string? LastRequestedCwd { get; private set; }

        public ValueTask<ILocalTerminalSession> GetOrCreateAsync(
            string conversationId,
            string preferredCwd,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequestedCwd = preferredCwd;
            if (!_sessions.TryGetValue(conversationId, out var session))
            {
                session = new RecordingLocalTerminalSession(conversationId, preferredCwd);
                _sessions.Add(conversationId, session);
            }

            return ValueTask.FromResult<ILocalTerminalSession>(session);
        }

        public ValueTask DisposeConversationAsync(string conversationId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sessions.Remove(conversationId);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _sessions.Clear();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLocalTerminalSession : ILocalTerminalSession
    {
        public RecordingLocalTerminalSession(string conversationId, string currentWorkingDirectory)
        {
            ConversationId = conversationId;
            CurrentWorkingDirectory = currentWorkingDirectory;
        }

        public string ConversationId { get; }

        public string CurrentWorkingDirectory { get; }

        public LocalTerminalTransportMode TransportMode => LocalTerminalTransportMode.PseudoConsole;

        public bool CanAcceptInput => true;

        public event EventHandler<string>? OutputReceived;

        public event EventHandler? StateChanged;

        public ValueTask WriteInputAsync(string input, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OutputReceived?.Invoke(this, input);
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DisplayCatalogTestScope : IDisposable, IAsyncDisposable
    {
        private readonly IState<ConversationAttentionState> _attentionState;

        public DisplayCatalogTestScope(
            IConversationCatalogReadModel catalog,
            IUiDispatcher uiDispatcher)
        {
            _attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
            Presenter = new ConversationCatalogDisplayPresenter(
                catalog,
                new ConversationAttentionStore(_attentionState),
                uiDispatcher);
        }

        public ConversationCatalogDisplayPresenter Presenter { get; }

        public void Dispose()
        {
            Presenter.Dispose();
            _attentionState.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            Presenter.Dispose();
            await _attentionState.DisposeAsync();
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
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns(tcsPrompt.Task);

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;

        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

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
    public async Task SendPromptAsync_WhenInvokedOffUiThread_QueuesCurrentPromptClearAndRestoreOnUiDispatcher()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        var promptDispatch = new TaskCompletionSource<AcpPromptDispatchResult>();

        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                "remote-1",
                "/plan",
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(promptDispatch.Task);

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(CreateConnectedChatService().Object));
        fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio });

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        viewModel.CurrentPrompt = "/plan";
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        var sendTask = Task.Run(() => viewModel.SendPromptCommand.ExecuteAsync(null));

        await WaitForConditionAsync(() => Task.FromResult(syncContext.PendingCount > 0));

        Assert.Equal("/plan", viewModel.CurrentPrompt);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(string.IsNullOrEmpty(viewModel.CurrentPrompt));
        });

        promptDispatch.SetException(new TimeoutException("timed out"));

        await WaitForConditionAsync(() => Task.FromResult(syncContext.PendingCount > 0));

        Assert.Equal(string.Empty, viewModel.CurrentPrompt);

        await syncContext.RunUntilCompletedAsync(sendTask);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(string.Equals(viewModel.CurrentPrompt, "/plan", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task SendPromptAsync_UsesCanonicalUuidFormat_ForPromptMessageId()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        string? capturedPromptMessageId = null;

        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                "remote-1",
                "hello",
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, IAcpChatCoordinatorSink, Func<CancellationToken, Task<bool>>, CancellationToken>(
                (_, _, promptMessageId, _, _, _) => capturedPromptMessageId = promptMessageId)
            .ReturnsAsync(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(StopReason.EndTurn, "11111111-1111-1111-1111-111111111111"), false));

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(CreateConnectedChatService().Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        viewModel.CurrentPrompt = "hello";
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        await AwaitWithSynchronizationContextAsync(
            syncContext,
            viewModel.SendPromptCommand.ExecuteAsync(null));

        Assert.False(string.IsNullOrWhiteSpace(capturedPromptMessageId));
        Assert.True(Guid.TryParse(capturedPromptMessageId, out _));
        Assert.Contains("-", capturedPromptMessageId!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendPromptAsync_WhenPromptResponseOmitsUserMessageId_DoesNotPersistClientRequestId()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        string? capturedPromptMessageId = null;

        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                "remote-1",
                "hello",
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, IAcpChatCoordinatorSink, Func<CancellationToken, Task<bool>>, CancellationToken>(
                (_, _, promptMessageId, _, _, _) => capturedPromptMessageId = promptMessageId)
            .ReturnsAsync(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(StopReason.EndTurn), false));

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(CreateConnectedChatService().Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        viewModel.CurrentPrompt = "hello";
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        await AwaitWithSynchronizationContextAsync(
            syncContext,
            viewModel.SendPromptCommand.ExecuteAsync(null));

        var state = await fixture.GetStateAsync();
        var transcript = state.ResolveContentSlice("conv-1")?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var outgoing = Assert.Single(transcript);
        Assert.Equal("hello", outgoing.TextContent);
        Assert.Null(outgoing.ProtocolMessageId);
        Assert.False(string.IsNullOrWhiteSpace(capturedPromptMessageId));
        Assert.Equal(capturedPromptMessageId, state.ActiveTurn!.PendingUserProtocolMessageId);
    }

    [Fact]
    public async Task SendPromptAsync_WhenAuthoritativeUserMessageUpdateArrivesBeforeUnacknowledgedPromptResponse_PreservesServerMessageId()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        var tcsSession = new TaskCompletionSource<AcpRemoteSessionResult>();
        var tcsPrompt = new TaskCompletionSource<AcpPromptDispatchResult>();

        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcsSession.Task);
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcsPrompt.Task);

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));
        fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-1", Name = "Profile 1", Transport = TransportType.Stdio });

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                string.Equals(viewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)
                && viewModel.IsSessionActive
                && string.Equals(viewModel.SelectedProfileId, "profile-1", StringComparison.Ordinal)
                && viewModel.IsInitialized);
        }, timeoutMilliseconds: 5000);

        viewModel.CurrentPrompt = "hello";
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(viewModel.CanSendPromptUi);
        }, timeoutMilliseconds: 5000);

        var sendTask = viewModel.SendPromptCommand.ExecuteAsync(null);

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return (await fixture.GetStateAsync()).ActiveTurn?.Phase == ChatTurnPhase.CreatingRemoteSession;
        });

        tcsSession.SetResult(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return (await fixture.GetStateAsync()).ActiveTurn?.Phase == ChatTurnPhase.WaitingForAgent;
        });
        await fixture.DispatchAsync(new SetBindingSliceAction(new ConversationBindingSlice("conv-1", "remote-1", null)));
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new UserMessageUpdate(new TextContentBlock("hello"))
            {
                MessageId = "22222222-2222-2222-2222-222222222222"
            }));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var transcript = (await fixture.GetStateAsync()).ResolveContentSlice("conv-1")?.Transcript
                ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            return transcript.Count == 1
                && string.Equals(transcript[0].ProtocolMessageId, "22222222-2222-2222-2222-222222222222", StringComparison.Ordinal);
        });

        tcsPrompt.SetResult(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(StopReason.EndTurn), false));
        await sendTask;

        var finalTranscript = (await fixture.GetStateAsync()).ResolveContentSlice("conv-1")?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var outgoing = Assert.Single(finalTranscript);
        Assert.Equal("22222222-2222-2222-2222-222222222222", outgoing.ProtocolMessageId);
    }

    [Fact]
    public async Task SendPromptAsync_WhenRemoteEchoesUserMessageChunk_DoesNotAppendDuplicateOutgoingMessage()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        var tcsSession = new TaskCompletionSource<AcpRemoteSessionResult>();
        var tcsPrompt = new TaskCompletionSource<AcpPromptDispatchResult>();

        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcsSession.Task);
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcsPrompt.Task);

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        viewModel.CurrentPrompt = "hello";
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        var sendTask = viewModel.SendPromptCommand.ExecuteAsync(null);

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return (await fixture.GetStateAsync()).ActiveTurn?.Phase == ChatTurnPhase.CreatingRemoteSession;
        });

        tcsSession.SetResult(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return (await fixture.GetStateAsync()).ActiveTurn?.Phase == ChatTurnPhase.WaitingForAgent;
        });
        await fixture.DispatchAsync(new SetBindingSliceAction(new ConversationBindingSlice("conv-1", "remote-1", null)));
        syncContext.RunAll();

        var baselineUpsertCount = fixture.ChatStore.Actions.OfType<UpsertTranscriptMessageAction>().Count();
        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new UserMessageUpdate(new TextContentBlock("hello"))));

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            var currentUpsertCount = fixture.ChatStore.Actions.OfType<UpsertTranscriptMessageAction>().Count();
            return Task.FromResult(currentUpsertCount > baselineUpsertCount);
        });

        var transcript = (await fixture.GetStateAsync()).ResolveContentSlice("conv-1")?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.Single(transcript.Where(message =>
            message.IsOutgoing
            && string.Equals(message.TextContent, "hello", StringComparison.Ordinal)));

        tcsPrompt.SetResult(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(), false));
        await sendTask;
    }

    [Fact]
    public async Task SendPromptAsync_WhenUserMessageUpdateArrivesBeforePromptResponseWithDifferentAuthoritativeId_ReusesOptimisticOutgoingMessage()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        var tcsSession = new TaskCompletionSource<AcpRemoteSessionResult>();
        var tcsPrompt = new TaskCompletionSource<AcpPromptDispatchResult>();

        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcsSession.Task);
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcsPrompt.Task);

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        viewModel.CurrentPrompt = "hello";
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        var sendTask = viewModel.SendPromptCommand.ExecuteAsync(null);

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return (await fixture.GetStateAsync()).ActiveTurn?.Phase == ChatTurnPhase.CreatingRemoteSession;
        });

        tcsSession.SetResult(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return (await fixture.GetStateAsync()).ActiveTurn?.Phase == ChatTurnPhase.WaitingForAgent;
        });
        await fixture.DispatchAsync(new SetBindingSliceAction(new ConversationBindingSlice("conv-1", "remote-1", null)));
        syncContext.RunAll();

        var optimisticTranscript = (await fixture.GetStateAsync()).ResolveContentSlice("conv-1")?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var optimisticMessageId = Assert.Single(optimisticTranscript).Id;

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new UserMessageUpdate(new TextContentBlock("hello"))
            {
                MessageId = "server-auth-1"
            }));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var transcript = (await fixture.GetStateAsync()).ResolveContentSlice("conv-1")?.Transcript
                ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            return transcript.Count == 1
                && string.Equals(transcript[0].Id, optimisticMessageId, StringComparison.Ordinal)
                && string.Equals(transcript[0].ProtocolMessageId, "server-auth-1", StringComparison.Ordinal);
        });

        tcsPrompt.SetResult(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(StopReason.EndTurn, "server-auth-1"), false));
        await sendTask;

        var finalTranscript = (await fixture.GetStateAsync()).ResolveContentSlice("conv-1")?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.Single(finalTranscript);
        Assert.Equal(optimisticMessageId, finalTranscript[0].Id);
        Assert.Equal("server-auth-1", finalTranscript[0].ProtocolMessageId);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_UserMessageUpdate_AfterPromptCompletes_WithDifferentAuthoritativeId_ReusesExistingOptimisticOutgoingMessage()
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
            ActiveTurn = new ActiveTurnState(
                "conv-1",
                "turn-1",
                ChatTurnPhase.Completed,
                DateTime.UtcNow,
                DateTime.UtcNow,
                PendingUserMessageLocalId: "local-1",
                PendingUserProtocolMessageId: "client-request-1",
                PendingUserMessageText: "hello"),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "conv-1",
                new ConversationContentSlice(
                    ImmutableList.Create(new ConversationMessageSnapshot
                    {
                        Id = "local-1",
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "hello",
                        ProtocolMessageId = null,
                        Timestamp = new DateTime(2026, 4, 23, 0, 0, 0, DateTimeKind.Utc)
                    }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null))
        };
        await fixture.UpdateStateAsync(_ => initialState);
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new UserMessageUpdate(new TextContentBlock("hello"))
            {
                MessageId = "server-auth-77"
            }));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var transcript = (await fixture.GetStateAsync()).ResolveContentSlice("conv-1")?.Transcript
                ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            return transcript.Count == 1
                && string.Equals(transcript[0].Id, "local-1", StringComparison.Ordinal)
                && string.Equals(transcript[0].ProtocolMessageId, "server-auth-77", StringComparison.Ordinal);
        });

        var state = await fixture.GetStateAsync();
        var transcript = state.ResolveContentSlice("conv-1")?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.Single(transcript);
        Assert.Equal("local-1", transcript[0].Id);
        Assert.Equal("server-auth-77", transcript[0].ProtocolMessageId);
        Assert.Equal("hello", transcript[0].TextContent);
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
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(), false));

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(CreateConnectedChatService().Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                string.Equals(viewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)
                && viewModel.IsSessionActive
                && viewModel.IsInitialized);
        });

        viewModel.CurrentPrompt = "show modes";
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(viewModel.CanSendPromptUi);
        });

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
    public async Task SendPromptAsync_WhenSessionNewUsesNonCanonicalConfigOptions_IgnoresLegacyModesPerAcp()
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
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(), false));

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(CreateConnectedChatService().Object));

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
                viewModel.AvailableModes.Count == 0
                && viewModel.SelectedMode is null
                && viewModel.ConfigOptions.Count == 2);
        });

        Assert.Empty(viewModel.AvailableModes);
        Assert.Null(viewModel.SelectedMode);
        Assert.Equal(2, viewModel.ConfigOptions.Count);
    }

    [Fact]
    public async Task SendPromptAsync_WhenPromptResponseIsCancelled_PreservesCancelledTurnPhase()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.EnsureRemoteSessionAsync(It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(StopReason.Cancelled), false));

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(CreateConnectedChatService().Object));

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
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(viewModel.CanSendPromptUi);
        }, timeoutMilliseconds: 2000);

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
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(CreateConnectedChatService().Object));

        var turnStartedAt = new DateTime(2026, 3, 24, 3, 0, 0, DateTimeKind.Utc);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "conv-1",
                new ConversationContentSlice(
                    ImmutableList.Create(
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
                            Title = "current",
                            ToolCallContent = new List<ToolCallContent>
                            {
                                new ContentToolCallContent(new ResourceLinkContentBlock("https://example.com/tool-current"))
                            }
                        }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null)),
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
            var transcript = state.ResolveContentSlice("conv-1")?.Transcript
                ?? state.Transcript
                ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            var cancelledCurrent = transcript.Any(message =>
                string.Equals(message.Id, "tool-current", StringComparison.Ordinal)
                && message.ToolCallStatus == ToolCallStatus.Cancelled);
            return state.ActiveTurn?.Phase == ChatTurnPhase.Cancelled && cancelledCurrent;
        }, timeoutMilliseconds: 12000);

        var finalState = await fixture.GetStateAsync();
        var finalTranscript = finalState.ResolveContentSlice("conv-1")?.Transcript
            ?? finalState.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.Equal(ChatTurnPhase.Cancelled, finalState.ActiveTurn!.Phase);
        Assert.Equal(ToolCallStatus.InProgress, finalTranscript.Single(message => string.Equals(message.Id, "tool-old", StringComparison.Ordinal)).ToolCallStatus);
        var cancelledCurrent = finalTranscript.Single(message => string.Equals(message.Id, "tool-current", StringComparison.Ordinal));
        Assert.Equal(ToolCallStatus.Cancelled, cancelledCurrent.ToolCallStatus);
        var structuredContent = Assert.Single(cancelledCurrent.ToolCallContent!);
        var content = Assert.IsType<ContentToolCallContent>(structuredContent);
        var resourceLink = Assert.IsType<ResourceLinkContentBlock>(content.Content);
        Assert.Equal("https://example.com/tool-current", resourceLink.Uri);

        commands.Verify(x => x.CancelPromptAsync(It.IsAny<IAcpChatCoordinatorSink>(), "User cancelled"), Times.Once);
    }

    [Fact]
    public async Task CancelPromptCommand_WhenPermissionRequestIsPending_RespondsCancelledAndClearsDialog()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.CancelPromptAsync(It.IsAny<IAcpChatCoordinatorSink>(), "User cancelled"))
            .Returns(Task.CompletedTask);

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        chatService.Setup(service => service.RespondToPermissionRequestAsync("permission-1", "cancelled", null))
            .ReturnsAsync(true);
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            IsPromptInFlight = true
        });
        syncContext.RunAll();

        chatService.Raise(service => service.PermissionRequestReceived += null, new PermissionRequestEventArgs
        {
            MessageId = "permission-1",
            SessionId = "remote-1",
            Options = []
        });
        syncContext.RunAll();
        Assert.True(viewModel.ShowPermissionDialog);
        Assert.NotNull(viewModel.PendingPermissionRequest);

        await viewModel.CancelPromptCommand.ExecuteAsync(null);
        syncContext.RunAll();

        chatService.Verify(service => service.RespondToPermissionRequestAsync("permission-1", "cancelled", null), Times.Once);
        Assert.False(viewModel.ShowPermissionDialog);
        Assert.Null(viewModel.PendingPermissionRequest);
    }

    [Fact]
    public async Task CancelSessionCommand_WhenMatchingPermissionRequestIsPending_RespondsCancelled()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        chatService.Setup(service => service.CancelSessionAsync(It.IsAny<SessionCancelParams>()))
            .ReturnsAsync(new SessionCancelResponse(true));
        chatService.Setup(service => service.RespondToPermissionRequestAsync("permission-2", "cancelled", null))
            .ReturnsAsync(true);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-fresh", "profile-a"))
        });
        syncContext.RunAll();

        chatService.Raise(service => service.PermissionRequestReceived += null, new PermissionRequestEventArgs
        {
            MessageId = "permission-2",
            SessionId = "remote-fresh",
            Options = []
        });

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.ViewModel.ShowPermissionDialog);
        }, timeoutMilliseconds: 5000);

        await fixture.ViewModel.CancelSessionCommand.ExecuteAsync(null);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                !fixture.ViewModel.ShowPermissionDialog
                && fixture.ViewModel.PendingPermissionRequest is null);
        }, timeoutMilliseconds: 5000);

        chatService.Verify(service => service.RespondToPermissionRequestAsync("permission-2", "cancelled", null), Times.Once);
        chatService.Verify(service => service.CancelSessionAsync(It.Is<SessionCancelParams>(p => p.SessionId == "remote-fresh")), Times.Once);
        Assert.False(fixture.ViewModel.ShowPermissionDialog);
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
    }

    [Fact]
    public async Task SendPromptAsync_WhenPromptResponseReturnsUserMessageId_ReconcilesOptimisticOutgoingMessage()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        string? capturedPromptMessageId = null;
        commands.Setup(x => x.EnsureRemoteSessionAsync(It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<System.Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, IAcpChatCoordinatorSink, System.Func<CancellationToken, Task<bool>>, CancellationToken>(
                (_, _, promptMessageId, _, _, _) => capturedPromptMessageId = promptMessageId)
            .ReturnsAsync(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(StopReason.EndTurn, "user-auth-1"), false));

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(CreateConnectedChatService().Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
        });
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        viewModel.CurrentPrompt = "hello";
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(viewModel.CanSendPromptUi);
        }, timeoutMilliseconds: 5000);

        await viewModel.SendPromptCommand.ExecuteAsync(null);

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var transcript = (await fixture.GetStateAsync()).ResolveContentSlice("conv-1")?.Transcript
                ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            return transcript.Count == 1
                && string.Equals(transcript[0].ProtocolMessageId, "user-auth-1", StringComparison.Ordinal);
        }, timeoutMilliseconds: 5000);

        var state = await fixture.GetStateAsync();
        var transcript = state.ResolveContentSlice("conv-1")?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.Single(transcript);
        Assert.Equal("hello", transcript[0].TextContent);
        Assert.Equal("user-auth-1", transcript[0].ProtocolMessageId);
        Assert.False(string.IsNullOrWhiteSpace(capturedPromptMessageId));
        commands.Verify(
            x => x.DispatchPromptToRemoteSessionAsync(
                "remote-1",
                "hello",
                capturedPromptMessageId,
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<System.Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_UserMessageUpdate_AfterPromptCompletes_WithMatchingUserMessageId_ReusesExistingOutgoingMessage()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ActiveTurn = new ActiveTurnState(
                "conv-1",
                "turn-1",
                ChatTurnPhase.Completed,
                DateTime.UtcNow,
                DateTime.UtcNow,
                PendingUserMessageLocalId: "local-1",
                PendingUserProtocolMessageId: "user-auth-1",
                PendingUserMessageText: "local draft text"),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "conv-1",
                new ConversationContentSlice(
                    ImmutableList.Create(new ConversationMessageSnapshot
                    {
                        Id = "local-1",
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "local optimistic text",
                        Timestamp = new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc)
                    }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null))
        };
        await fixture.UpdateStateAsync(_ => initialState);
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new UserMessageUpdate(new TextContentBlock("server echoed hello"))
            {
                MessageId = "user-auth-1"
            }));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var transcript = (await fixture.GetStateAsync()).ResolveContentSlice("conv-1")?.Transcript
                ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            return transcript.Count == 1
                && string.Equals(transcript[0].Id, "local-1", StringComparison.Ordinal)
                && string.Equals(transcript[0].ProtocolMessageId, "user-auth-1", StringComparison.Ordinal);
        });

        var state = await fixture.GetStateAsync();
        var transcript = state.ResolveContentSlice("conv-1")?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.Single(transcript);
        Assert.Equal("local-1", transcript[0].Id);
        Assert.Equal("user-auth-1", transcript[0].ProtocolMessageId);
        Assert.Equal("server echoed hello", transcript[0].TextContent);
    }

    [Fact]
    public async Task SendPromptAsync_WhenPromptResponseIsRefusal_PreservesFailedTurnWithReason()
    {
        var syncContext = new QueueingSynchronizationContext();
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.EnsureRemoteSessionAsync(It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpRemoteSessionResult("remote-1", new SessionNewResponse("remote-1"), false));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<System.Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcpPromptDispatchResult("remote-1", new SessionPromptResponse(StopReason.Refusal), false));

        await using var fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        var viewModel = fixture.ViewModel;
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(CreateConnectedChatService().Object));

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
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

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
    public async Task ProcessSessionUpdateAsync_UserMessageUpdate_WithMatchingProtocolMessageId_ReusesExistingOutgoingMessage()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "conv-1",
                new ConversationContentSlice(
                    ImmutableList.Create(new ConversationMessageSnapshot
                    {
                        Id = "local-1",
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "hello",
                        ProtocolMessageId = "msg-1",
                        Timestamp = new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc)
                    }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null))
        };
        await fixture.UpdateStateAsync(_ => initialState);
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new UserMessageUpdate(new TextContentBlock("hello"))
            {
                MessageId = "msg-1"
            }));

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var transcript = (await fixture.GetStateAsync()).ResolveContentSlice("conv-1")?.Transcript
                ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            return transcript.Count == 1
                && string.Equals(transcript[0].Id, "local-1", StringComparison.Ordinal)
                && string.Equals(transcript[0].ProtocolMessageId, "msg-1", StringComparison.Ordinal);
        });

        var state = await fixture.GetStateAsync();
        var transcript = state.ResolveContentSlice("conv-1")?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.Single(transcript);
        Assert.Equal("local-1", transcript[0].Id);
        Assert.Equal("msg-1", transcript[0].ProtocolMessageId);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_ToolCallUpdate_AdvancesStoreBackedTurnToToolPending()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

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
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

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
    public async Task SessionUpdateReceived_AgentMessageUpdate_UpdatesStoreBeforeUiPumpRuns()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

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
            new SessionUpdateEventArgs("remote-1", new AgentMessageUpdate(new TextContentBlock("hello from remote"))));

        var stateBeforeUiPump = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.Responding, stateBeforeUiPump.ActiveTurn?.Phase);
        var transcriptBeforeUiPump = stateBeforeUiPump.ResolveContentSlice("conv-1")?.Transcript
            ?? stateBeforeUiPump.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.Contains(
            transcriptBeforeUiPump,
            message => string.Equals(message.TextContent, "hello from remote", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SessionUpdateReceived_AgentMessageUpdate_DoesNotProjectTranscriptUntilUiPumpRuns()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

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
            new SessionUpdateEventArgs("remote-1", new AgentMessageUpdate(new TextContentBlock("hello from remote"))));

        Assert.DoesNotContain(
            viewModel.MessageHistory,
            message => string.Equals(message.TextContent, "hello from remote", StringComparison.Ordinal));

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(viewModel.MessageHistory.Any(
                message => string.Equals(message.TextContent, "hello from remote", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_ToolCallStatusCancelled_AdvancesStoreBackedTurnToCancelled()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));
        syncContext.RunAll();

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
            var transcript = state.ResolveContentSlice("conv-1")?.Transcript
                ?? state.Transcript
                ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            return transcript.Any(message =>
                string.Equals(message.ToolCallId, "call-1", StringComparison.Ordinal)
                && message.ToolCallStatus == ToolCallStatus.Cancelled);
        });

        var state = await fixture.GetStateAsync();
        var transcript = state.ResolveContentSlice("conv-1")?.Transcript
            ?? state.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        Assert.True(state.ActiveTurn is null || state.ActiveTurn.Phase == ChatTurnPhase.Cancelled);
        Assert.Contains(
            transcript,
            message => string.Equals(message.ToolCallId, "call-1", StringComparison.Ordinal)
                && message.ToolCallStatus == ToolCallStatus.Cancelled);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_ToolCallStatusUpdate_CreatesTranscriptEntryFromIncrementalSchemaFields()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

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
        var chatService = CreateConnectedChatService();
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await Task.Delay(50);

        const string conversationId = "conv-1";
        var updatedAt = "2026-03-24T03:00:00Z";

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-1",
                new SessionInfoUpdate
                {
                    Title = "Renamed by agent",
                    UpdatedAt = updatedAt
                }));

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
    public async Task ProcessSessionUpdateAsync_SessionInfoUpdate_WithCwd_DoesNotOverrideEstablishedSessionSetup()
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
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Original title",
                Cwd = @"C:\repo\demo",
                UpdatedAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            }));
        var chatService = CreateConnectedChatService();
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-1",
                new SessionInfoUpdate
                {
                    Title = "Renamed by agent",
                    UpdatedAt = "2026-03-24T03:00:00Z"
                }));

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            return string.Equals(
                state.ResolveSessionStateSlice("conv-1")?.SessionInfo?.Title,
                "Renamed by agent",
                StringComparison.Ordinal);
        });

        Assert.Equal(@"C:\repo\demo", sessions["conv-1"].Cwd);
        var finalState = await fixture.GetStateAsync();
        var storeSessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(
            finalState.ResolveSessionStateSlice("conv-1")!.Value.SessionInfo);
        var workspaceSessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(
            fixture.Workspace.GetConversationSnapshot("conv-1")!.SessionInfo);
        Assert.Equal("Renamed by agent", storeSessionInfo.Title);
        Assert.Equal(@"C:\repo\demo", storeSessionInfo.Cwd);
        Assert.Equal(storeSessionInfo.Cwd, workspaceSessionInfo.Cwd);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_SessionInfoUpdate_PreservesWhitespaceFieldsConsistentlyAcrossStoreAndWorkspace()
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
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Original title",
                Description = "Original description",
                Cwd = @"C:\repo\demo",
                UpdatedAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["existing"] = "value",
                    ["shared"] = "before"
                }
            }));
        var chatService = CreateConnectedChatService();
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty
                .Add("conv-1", new ConversationSessionStateSlice(
                    ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    new ConversationSessionInfoSnapshot
                    {
                        Title = "Original title",
                        Description = "Original description",
                        Cwd = @"C:\repo\demo",
                        UpdatedAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["existing"] = "value",
                            ["shared"] = "before"
                        }
                    },
                    null))
        });
        await Task.Delay(50);

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-1",
                new SessionInfoUpdate
                {
                    Title = string.Empty,
                    UpdatedAt = "2026-03-24T03:00:00Z",
                    Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["shared"] = "after",
                        ["added"] = 2
                    }
                }));

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            var sessionState = state.ResolveSessionStateSlice("conv-1");
            var workspaceSnapshot = fixture.Workspace.GetConversationSnapshot("conv-1");
            return sessionState?.SessionInfo?.Meta?.ContainsKey("added") == true
                && workspaceSnapshot?.SessionInfo?.Meta?.ContainsKey("added") == true;
        });

        var finalState = await fixture.GetStateAsync();
        var storeSessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(
            finalState.ResolveSessionStateSlice("conv-1")!.Value.SessionInfo);
        var workspaceSessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(
            fixture.Workspace.GetConversationSnapshot("conv-1")!.SessionInfo);

        Assert.Equal("Original title", storeSessionInfo.Title);
        Assert.Equal("Original description", storeSessionInfo.Description);
        Assert.Equal(@"C:\repo\demo", storeSessionInfo.Cwd);
        Assert.Equal(storeSessionInfo.Title, workspaceSessionInfo.Title);
        Assert.Equal(storeSessionInfo.Description, workspaceSessionInfo.Description);
        Assert.Equal(storeSessionInfo.Cwd, workspaceSessionInfo.Cwd);
        Assert.Equal(storeSessionInfo.UpdatedAtUtc, workspaceSessionInfo.UpdatedAtUtc);
        Assert.Equal(storeSessionInfo.Meta!["existing"], workspaceSessionInfo.Meta!["existing"]);
        Assert.Equal(storeSessionInfo.Meta["shared"], workspaceSessionInfo.Meta["shared"]);
        Assert.Equal(storeSessionInfo.Meta["added"], workspaceSessionInfo.Meta["added"]);
    }

    private sealed class FakeVoiceInputService : IVoiceInputService
    {
        public bool IsSupported { get; set; }

        public bool IsListening { get; private set; }

        public int PermissionRequestCount { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int AuthorizationHelpRequestCount { get; private set; }

        public bool ThrowIfStopCalledAfterCallerCancellation { get; set; }

        public List<string> StartedSessionIds { get; } = new();

        public VoiceInputPermissionResult PermissionResult { get; set; } =
            new(VoiceInputPermissionStatus.Unsupported, "Not configured");

        public event EventHandler<VoiceInputPartialResult>? PartialResultReceived;

        public event EventHandler<VoiceInputFinalResult>? FinalResultReceived;

        public event EventHandler<VoiceInputSessionEndedResult>? SessionEnded;

        public event EventHandler<VoiceInputErrorResult>? ErrorOccurred;

        private CancellationToken _lastStartCancellationToken;

        public Task<VoiceInputPermissionResult> EnsurePermissionAsync(CancellationToken cancellationToken = default)
        {
            PermissionRequestCount++;
            return Task.FromResult(PermissionResult);
        }

        public Task StartAsync(VoiceInputSessionOptions options, CancellationToken cancellationToken = default)
        {
            StartCount++;
            IsListening = true;
            _lastStartCancellationToken = cancellationToken;
            StartedSessionIds.Add(options.RequestId);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowIfStopCalledAfterCallerCancellation && _lastStartCancellationToken.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(CancellationTokenSource), "The CancellationTokenSource has been disposed.");
            }

            StopCount++;
            IsListening = false;
            return Task.CompletedTask;
        }

        public Task<bool> TryRequestAuthorizationHelpAsync(CancellationToken cancellationToken = default)
        {
            AuthorizationHelpRequestCount++;
            return Task.FromResult(true);
        }

        public void EmitPartial(VoiceInputPartialResult result) => PartialResultReceived?.Invoke(this, result);

        public void EmitFinal(VoiceInputFinalResult result)
        {
            IsListening = false;
            FinalResultReceived?.Invoke(this, result);
        }

        public void EmitError(VoiceInputErrorResult result)
        {
            IsListening = false;
            ErrorOccurred?.Invoke(this, result);
        }

        public void EmitSessionEnded(VoiceInputSessionEndedResult result)
        {
            IsListening = false;
            SessionEnded?.Invoke(this, result);
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    [Fact]
    public async Task RefreshMiniWindowSessions_ProjectsCompactDisplayNameWithoutChangingDisplayName()
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

        await sessionManager.Object.CreateSessionAsync("conv-compact", @"C:\repo\compact");
        sessionManager.Object.UpdateSession(
            "conv-compact",
            session => session.DisplayName = "This session title should stay complete while the mini window trims it",
            updateActivity: false);

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-compact",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-compact"
        });

        var item = fixture.ViewModel.MiniWindowSessions.Single(entry =>
            string.Equals(entry.ConversationId, "conv-compact", StringComparison.Ordinal));

        Assert.Equal("This session title should stay complete while the mini window trims it", item.DisplayName);
        Assert.NotEqual(item.DisplayName, item.CompactDisplayName);
        Assert.EndsWith("...", item.CompactDisplayName, StringComparison.Ordinal);
        Assert.True(item.CompactDisplayName.Length <= 24);
        Assert.DoesNotContain("conv-compact", item.CompactDisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshMiniWindowSessions_PreservesWholeGraphemeClustersWhenCompactDisplayNameTrims()
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

        const string fullDisplayName = "12345678901234567890👨‍👩‍👧‍👦 trailing title words";
        await sessionManager.Object.CreateSessionAsync("conv-emoji", @"C:\repo\emoji");
        sessionManager.Object.UpdateSession(
            "conv-emoji",
            session => session.DisplayName = fullDisplayName,
            updateActivity: false);

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-emoji",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-emoji"
        });

        var item = fixture.ViewModel.MiniWindowSessions.Single(entry =>
            string.Equals(entry.ConversationId, "conv-emoji", StringComparison.Ordinal));

        Assert.Equal(fullDisplayName, item.DisplayName);
        Assert.Equal("12345678901234567890👨‍👩‍👧‍👦...", item.CompactDisplayName);
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
    public async Task ProcessSessionUpdateAsync_AvailableCommandsUpdate_SurvivesActiveConversationSwitches()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        syncContext.RunAll();

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-1",
                new AvailableCommandsUpdate
                {
                    AvailableCommands =
                    [
                        new AvailableCommand
                        {
                            Name = "plan",
                            Description = "Planning command",
                            Input = new AvailableCommandInput
                            {
                                Hint = "target"
                            }
                        }
                    ]
                }));

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                viewModel.AvailableSlashCommands.Count == 1
                && string.Equals(viewModel.AvailableSlashCommands[0].Name, "plan", StringComparison.Ordinal));
        });

        await fixture.DispatchAsync(new SelectConversationAction("conv-2"));
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(viewModel.AvailableSlashCommands.Count == 0);
        });

        await fixture.DispatchAsync(new SelectConversationAction("conv-1"));
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                viewModel.AvailableSlashCommands.Count == 1
                && string.Equals(viewModel.AvailableSlashCommands[0].Name, "plan", StringComparison.Ordinal));
        });

        var state = await fixture.GetStateAsync();
        var sessionState = Assert.NotNull(state.ResolveSessionStateSlice("conv-1"));
        var command = Assert.Single(sessionState!.AvailableCommands);
        Assert.Equal("plan", command.Name);
        Assert.Equal("Planning command", command.Description);
        Assert.Equal("target", command.InputHint);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_UsageUpdate_RetainsUsageInSessionStateWithoutUnhandledLog()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));

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
            new SessionUpdateEventArgs(
                "remote-1",
                new UsageUpdate
                {
                    Used = 7,
                    Size = 128,
                    Cost = new UsageCost
                    {
                        Amount = 1.5m,
                        Currency = "USD"
                    }
                }));

        await Task.Delay(50);
        syncContext.RunAll();

        var state = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.WaitingForAgent, state.ActiveTurn!.Phase);
        var sessionState = Assert.NotNull(state.ResolveSessionStateSlice("conv-1"));
        Assert.NotNull(sessionState!.Usage);
        Assert.Equal(7, sessionState.Usage!.Used);
        Assert.Equal(128, sessionState.Usage.Size);
        Assert.NotNull(sessionState.Usage.Cost);
        Assert.Equal(1.5m, sessionState.Usage.Cost!.Amount);
        Assert.Equal("USD", sessionState.Usage.Cost.Currency);

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

        ViewModelFixture? fixture = null;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                var activeFixture = fixture ?? throw new InvalidOperationException("Fixture must be initialized before connect callback runs.");
                await sink.SelectProfileAsync(profile, cancellationToken);
                await sink.ReplaceChatServiceAsync(chatService.Object, cancellationToken);
                await activeFixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                await WaitForConditionAsync(async () =>
                {
                    var connectionState = await activeFixture.GetConnectionStateAsync();
                    return connectionState.Phase == ConnectionPhase.Connected
                        && string.Equals(connectionState.ForegroundTransportProfileId, profile.Id, StringComparison.Ordinal);
                }, timeoutMilliseconds: 2000);
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });
        commands.Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected non-context ACP connect path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected remote session creation path."));
        commands.Setup(x => x.SendPromptAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.CancelPromptAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        commands.Setup(x => x.DisconnectAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\demo");
        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));

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
            await DispatchConnectedAsync(fixture, "profile-1");
            await WaitForConditionAsync(async () =>
                string.Equals((await fixture.GetStateAsync()).HydratedConversationId, "conv-1", StringComparison.Ordinal));

            var hydrated = await fixture.ViewModel.HydrateActiveConversationAsync();

            Assert.True(hydrated, fixture.ViewModel.ErrorMessage);
            Assert.NotNull(capturedParams);
            Assert.Equal("remote-1", capturedParams!.SessionId);
            Assert.Equal(@"C:\repo\demo", capturedParams.Cwd);
            Assert.NotNull(capturedParams.McpServers);
            Assert.Empty(capturedParams.McpServers!);
        }
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenSessionListReportsDifferentCwd_PreservesLoadedCwdPerAcpSessionSetup()
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

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                var activeFixture = fixture ?? throw new InvalidOperationException("Fixture must be initialized before connect callback runs.");
                await sink.SelectProfileAsync(profile, cancellationToken);
                await sink.ReplaceChatServiceAsync(chatService.Object, cancellationToken);
                await activeFixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                await WaitForConditionAsync(async () =>
                {
                    var connectionState = await activeFixture.GetConnectionStateAsync();
                    return connectionState.Phase == ConnectionPhase.Connected
                        && string.Equals(connectionState.ForegroundTransportProfileId, profile.Id, StringComparison.Ordinal);
                }, timeoutMilliseconds: 2000);
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });
        commands.Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected non-context ACP connect path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected remote session creation path."));
        commands.Setup(x => x.SendPromptAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.CancelPromptAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        commands.Setup(x => x.DisconnectAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\stale");
        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
            });
            await DispatchConnectedAsync(fixture, "profile-1");
            await WaitForConditionAsync(async () =>
                string.Equals((await fixture.GetStateAsync()).HydratedConversationId, "conv-1", StringComparison.Ordinal));

            var hydrated = await fixture.ViewModel.HydrateActiveConversationAsync();

            Assert.True(hydrated, fixture.ViewModel.ErrorMessage);
            Assert.NotNull(capturedParams);
            Assert.Equal(@"C:\repo\stale", capturedParams!.Cwd);
            await WaitForConditionAsync(async () =>
            {
                var state = await fixture.GetStateAsync();
                return string.Equals(
                    state.ResolveSessionStateSlice("conv-1")?.SessionInfo?.Title,
                    "Remote title",
                    StringComparison.Ordinal);
            }, timeoutMilliseconds: 2000);
            Assert.Equal(@"C:\repo\stale", sessions["conv-1"].Cwd);
            var finalState = await fixture.GetStateAsync();
            var storeSessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(
                finalState.ResolveSessionStateSlice("conv-1")!.Value.SessionInfo);
            var workspaceSessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(
                fixture.Workspace.GetConversationSnapshot("conv-1")!.SessionInfo);
            Assert.Equal("Remote title", storeSessionInfo.Title);
            Assert.Equal(@"C:\repo\stale", storeSessionInfo.Cwd);
            Assert.Equal(workspaceSessionInfo.Title, storeSessionInfo.Title);
            Assert.Equal(workspaceSessionInfo.Cwd, storeSessionInfo.Cwd);
            Assert.Equal(workspaceSessionInfo.UpdatedAtUtc, storeSessionInfo.UpdatedAtUtc);
        }
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenRemoteMetadataRefreshIsSlow_DoesNotBlockSessionLoad()
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

        var releaseSessionList = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadStarted = new TaskCompletionSource<SessionLoadParams?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(
            loadSession: true,
            sessionCapabilities: new SessionCapabilities
            {
                List = new SessionListCapabilities()
            }));
        chatService.Setup(service => service.ListSessionsAsync(It.IsAny<SessionListParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionListParams, CancellationToken>(async (_, cancellationToken) =>
            {
                await releaseSessionList.Task.WaitAsync(cancellationToken);
                return new SessionListResponse
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
                };
            });
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Callback<SessionLoadParams, CancellationToken>((value, _) => loadStarted.TrySetResult(value))
            .ReturnsAsync(SessionLoadResponse.Completed);

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();
        var capturedParams = await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(capturedParams);
        Assert.Equal(@"C:\repo\stale", capturedParams!.Cwd);

        var hydrated = await hydrationTask;
        Assert.True(hydrated, fixture.ViewModel.ErrorMessage);

        releaseSessionList.TrySetResult(null);

        await WaitForConditionAsync(async () =>
        {
            var storeState = await fixture.GetStateAsync();
            var sessionInfo = storeState.ResolveSessionStateSlice("conv-1")?.SessionInfo;
            return string.Equals(sessionInfo?.Title, "Remote title", StringComparison.Ordinal);
        }, timeoutMilliseconds: 2000);

        Assert.Equal(@"C:\repo\stale", sessions["conv-1"].Cwd);
        var finalState = await fixture.GetStateAsync();
        var storeSessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(
            finalState.ResolveSessionStateSlice("conv-1")!.Value.SessionInfo);
        Assert.Equal("Remote title", storeSessionInfo.Title);
        Assert.Equal(@"C:\repo\stale", storeSessionInfo.Cwd);
    }

    [Fact]
    public async Task RestoreAndHydrateRemoteConversation_WhenSessionListReportsDifferentCwd_NavigationKeepsProjectGrouping()
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

        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument
            {
                LastActiveConversationId = null,
                Conversations =
                {
                    new ConversationRecord
                    {
                        ConversationId = "conv-1",
                        DisplayName = "Remote title",
                        CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedAt = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                        Cwd = @"C:\repo\stale",
                        RemoteSessionId = "remote-1",
                        BoundProfileId = "profile-1"
                    }
                }
            });

        var releaseSessionList = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(
            loadSession: true,
            sessionCapabilities: new SessionCapabilities
            {
                List = new SessionListCapabilities()
            }));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);
        chatService.Setup(service => service.ListSessionsAsync(It.IsAny<SessionListParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionListParams?, CancellationToken>(async (_, cancellationToken) =>
            {
                await releaseSessionList.Task.WaitAsync(cancellationToken);
                return new SessionListResponse
                {
                    Sessions =
                    [
                        new AgentSessionInfo
                        {
                            SessionId = "remote-1",
                            Cwd = @"C:\Users\shang\AppData\Local\SalmonEgg",
                            Title = "Remote title",
                            UpdatedAt = "2026-03-28T12:34:56Z"
                        }
                    ]
                };
            });

        await using var fixture = CreateViewModel(syncContext, conversationStore: conversationStore, sessionManager: sessionManager);
        fixture.ViewModel.ReplaceChatService(chatService.Object);
        fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
        fixture.Preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = "project-1",
            Name = "Project One",
            RootPath = @"C:\repo\stale"
        });
        fixture.Preferences.ProjectPathMappings.Add(new ProjectPathMapping
        {
            ProfileId = "profile-1",
            RemoteRootPath = @"C:\repo\stale",
            LocalRootPath = @"C:\repo\stale"
        });

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1")).AsTask());
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected)).AsTask());

        var navState = new FakeNavigationPaneState();
        var selectionStore = new ShellSelectionStateStore();
        selectionStore.SetSelection(new NavigationSelectionState.Session("conv-1"));
        var runtimeState = new ShellNavigationRuntimeStateStore();
        var conversationCatalog = new ConversationCatalogFacade(
            GetPrivateFieldValue<ChatConversationWorkspace>(fixture.ViewModel, "_conversationWorkspace")!,
            new NavigationProjectPreferencesAdapter(fixture.Preferences),
            Mock.Of<IConversationActivationCoordinator>(),
            Mock.Of<IShellSelectionReadModel>(),
            new Lazy<INavigationCoordinator>(() => Mock.Of<INavigationCoordinator>()),
            GetConversationCatalogPresenter(fixture.ViewModel),
            NullLogger<ConversationCatalogFacade>.Instance);
        using var navVm = new MainNavigationViewModel(
            conversationCatalog,
            new NavigationProjectPreferencesAdapter(fixture.Preferences),
            Mock.Of<IUiInteractionService>(),
            Mock.Of<INavigationCoordinator>(),
            Mock.Of<ILogger<MainNavigationViewModel>>(),
            navState,
            Mock.Of<IShellLayoutMetricsSink>(),
            new NavigationSelectionProjector(),
            selectionStore,
            runtimeState,
            GetConversationCatalogPresenter(fixture.ViewModel),
            new ProjectAffinityResolver(),
            syncContext);

        navVm.RebuildTree();
        Assert.Equal("project-1", navVm.TryGetProjectIdForSession("conv-1"));

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();
        releaseSessionList.TrySetResult(null);

        await AwaitWithSynchronizationContextAsync(syncContext, hydrationTask);
        var hydrated = await hydrationTask;
        Assert.True(hydrated, fixture.ViewModel.ErrorMessage);

        await WaitForConditionAsync(
            () => Task.FromResult(
                string.Equals(fixture.Workspace.GetConversationSnapshot("conv-1")?.SessionInfo?.Cwd, @"C:\repo\stale", StringComparison.Ordinal)
                && string.Equals(GetConversationCatalogPresenter(fixture.ViewModel).Snapshot.SingleOrDefault()?.Cwd, @"C:\repo\stale", StringComparison.Ordinal)
                && string.Equals(sessionManager.Object.GetSession("conv-1")?.Cwd, @"C:\repo\stale", StringComparison.Ordinal)),
            timeoutMilliseconds: 4000);
        Assert.Equal(@"C:\repo\stale", fixture.Workspace.GetConversationSnapshot("conv-1")!.SessionInfo!.Cwd);
        Assert.Equal(@"C:\repo\stale", GetConversationCatalogPresenter(fixture.ViewModel).Snapshot.Single().Cwd);
        Assert.Equal("project-1", navVm.TryGetProjectIdForSession("conv-1"));
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenWorkspaceSnapshotCarriesEstablishedCwd_PreservesThatCwdAcrossSessionListRefresh()
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

        var conversationStore = new Mock<IConversationStore>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationDocument
            {
                LastActiveConversationId = null,
                Conversations =
                {
                    new ConversationRecord
                    {
                        ConversationId = "conv-1",
                        DisplayName = "Remote title",
                        CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedAt = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                        Cwd = @"C:\repo\stale",
                        RemoteSessionId = "remote-1",
                        BoundProfileId = "profile-1"
                    }
                }
            });

        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(
            loadSession: true,
            sessionCapabilities: new SessionCapabilities
            {
                List = new SessionListCapabilities()
            }));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);
        chatService.Setup(service => service.ListSessionsAsync(It.IsAny<SessionListParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionListResponse
            {
                Sessions =
                [
                    new AgentSessionInfo
                    {
                        SessionId = "remote-1",
                        Cwd = @"C:\Users\shang\AppData\Local\SalmonEgg",
                        Title = "Remote title",
                        UpdatedAt = "2026-03-28T12:34:56Z"
                    }
                ]
            });

        await using var fixture = CreateViewModel(syncContext, conversationStore: conversationStore, sessionManager: sessionManager);
        fixture.ViewModel.ReplaceChatService(chatService.Object);
        fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());
        sessions.Remove("conv-1");
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1")).AsTask());
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected)).AsTask());

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();
        await AwaitWithSynchronizationContextAsync(syncContext, hydrationTask);
        var hydrated = await hydrationTask;
        Assert.True(hydrated, fixture.ViewModel.ErrorMessage);

        Assert.Equal(@"C:\repo\stale", fixture.Workspace.GetConversationSnapshot("conv-1")!.SessionInfo!.Cwd);
        Assert.Equal(@"C:\repo\stale", GetConversationCatalogPresenter(fixture.ViewModel).Snapshot.Single().Cwd);
        Assert.Equal(@"C:\repo\stale", sessionManager.Object.GetSession("conv-1")!.Cwd);
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenOnlyRemoteSessionCarriesSetupCwd_PreservesThatCwdAcrossSessionListRefresh()
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

        await sessionManager.Object.CreateSessionAsync("remote-1", @"C:\repo\stale");

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
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                var activeFixture = fixture ?? throw new InvalidOperationException("Fixture must be initialized before connect callback runs.");
                await sink.SelectProfileAsync(profile, cancellationToken);
                await sink.ReplaceChatServiceAsync(chatService.Object, cancellationToken);
                await activeFixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });
        commands.Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected non-context ACP connect path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected remote session creation path."));
        commands.Setup(x => x.SendPromptAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.CancelPromptAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        commands.Setup(x => x.DisconnectAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
            fixture.Workspace.UpdateRemoteBinding("conv-1", "remote-1", "profile-1");

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
            });
            await DispatchConnectedAsync(fixture, "profile-1");

            var hydrated = await fixture.ViewModel.HydrateActiveConversationAsync();

            Assert.True(hydrated);
            await WaitForConditionAsync(async () =>
            {
                var state = await fixture.GetStateAsync();
                return string.Equals(
                    state.ResolveSessionStateSlice("conv-1")?.SessionInfo?.Title,
                    "Remote title",
                    StringComparison.Ordinal);
            }, timeoutMilliseconds: 2000);

            var storeSessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(
                (await fixture.GetStateAsync()).ResolveSessionStateSlice("conv-1")!.Value.SessionInfo);
            Assert.Equal(@"C:\repo\stale", storeSessionInfo.Cwd);
            Assert.Equal(@"C:\repo\stale", fixture.Workspace.GetConversationSnapshot("conv-1")!.SessionInfo!.Cwd);
        }
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_FollowedByWhitespaceSessionInfoUpdate_KeepsStoreAndWorkspaceSessionInfoAligned()
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
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                var activeFixture = fixture ?? throw new InvalidOperationException("Fixture must be initialized before connect callback runs.");
                await sink.SelectProfileAsync(profile, cancellationToken);
                sink.ReplaceChatService(chatService.Object);
                await activeFixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                await WaitForConditionAsync(async () =>
                {
                    var connectionState = await activeFixture.GetConnectionStateAsync();
                    return connectionState.Phase == ConnectionPhase.Connected
                        && string.Equals(connectionState.ForegroundTransportProfileId, profile.Id, StringComparison.Ordinal);
                }, timeoutMilliseconds: 2000);
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });
        commands.Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected non-context ACP connect path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected remote session creation path."));
        commands.Setup(x => x.SendPromptAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.CancelPromptAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        commands.Setup(x => x.DisconnectAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\stale");
        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            fixture.ViewModel.ReplaceChatService(chatService.Object);

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
            });
            await DispatchConnectedAsync(fixture, "profile-1");

            var hydrated = await fixture.ViewModel.HydrateActiveConversationAsync();
            Assert.True(hydrated);

            chatService.Raise(
                service => service.SessionUpdateReceived += null,
                new SessionUpdateEventArgs(
                    "remote-1",
                    new SessionInfoUpdate
                    {
                        Title = "   ",
                        UpdatedAt = "2026-03-29T01:02:03Z"
                    }));

            await WaitForConditionAsync(async () =>
            {
                var state = await fixture.GetStateAsync();
                return state.ResolveSessionStateSlice("conv-1")?.SessionInfo?.UpdatedAtUtc
                    == new DateTime(2026, 3, 29, 1, 2, 3, DateTimeKind.Utc);
            });

            var finalState = await fixture.GetStateAsync();
            var storeSessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(
                finalState.ResolveSessionStateSlice("conv-1")!.Value.SessionInfo);
            var workspaceSessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(
                fixture.Workspace.GetConversationSnapshot("conv-1")!.SessionInfo);

            Assert.Equal("Remote title", storeSessionInfo.Title);
            Assert.Equal(@"C:\repo\stale", storeSessionInfo.Cwd);
            Assert.Equal(storeSessionInfo.Title, workspaceSessionInfo.Title);
            Assert.Equal(storeSessionInfo.Cwd, workspaceSessionInfo.Cwd);
            Assert.Equal(storeSessionInfo.UpdatedAtUtc, workspaceSessionInfo.UpdatedAtUtc);
        }
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_SessionInfoUpdate_WithEmptyPayload_DoesNotCreateSessionInfoState()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-1",
                new SessionInfoUpdate
                {
                    Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                }));

        await Task.Delay(50);

        var state = await fixture.GetStateAsync();
        Assert.Null(state.ResolveSessionStateSlice("conv-1")?.SessionInfo);
        Assert.Null(fixture.Workspace.GetConversationSnapshot("conv-1")?.SessionInfo);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_SessionInfoUpdate_WithWhitespaceOnlyPayload_DoesNotCreateSessionInfoState()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var chatService = CreateConnectedChatService();
        fixture.ViewModel.ReplaceChatService(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs(
                "remote-1",
                new SessionInfoUpdate
                {
                    Title = "   ",
                    UpdatedAt = "   "
                }));

        await Task.Delay(50);

        var state = await fixture.GetStateAsync();
        Assert.Null(state.ResolveSessionStateSlice("conv-1")?.SessionInfo);
        Assert.Null(fixture.Workspace.GetConversationSnapshot("conv-1")?.SessionInfo);
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_PreservesAvailableCommandsAndUsageAcrossResync()
    {
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
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        var syncContext = new ImmediateSynchronizationContext();
        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                await sink.SelectProfileAsync(profile, cancellationToken);
                sink.ReplaceChatService(chatService.Object);
                await DispatchConnectedAsync(fixture!, profile.Id);
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });
        commands.Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected non-context ACP connect path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected remote session creation path."));
        commands.Setup(x => x.SendPromptAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.CancelPromptAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        commands.Setup(x => x.DisconnectAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            fixture.ViewModel.ReplaceChatService(chatService.Object);

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
                ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty
                    .Add("conv-1", new ConversationSessionStateSlice(
                        ImmutableList<ConversationModeOptionSnapshot>.Empty,
                        null,
                        ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                        false,
                        ImmutableList.Create(new ConversationAvailableCommandSnapshot("plan", "Planning command", "goal")),
                        null,
                        new ConversationUsageSnapshot(7, 128, new ConversationUsageCostSnapshot(1.5m, "USD"))))
            });
            await DispatchConnectedAsync(fixture, "profile-1");

            var hydrated = await fixture.ViewModel.HydrateActiveConversationAsync();

            Assert.True(hydrated);
            var finalState = await fixture.GetStateAsync();
            var sessionState = Assert.NotNull(finalState.ResolveSessionStateSlice("conv-1"));
            var command = Assert.Single(sessionState.AvailableCommands);
            Assert.Equal("plan", command.Name);
            Assert.NotNull(sessionState.Usage);
            Assert.Equal(7, sessionState.Usage!.Used);
            Assert.Equal(128, sessionState.Usage.Size);
            Assert.NotNull(sessionState.Usage.Cost);
            Assert.Equal(1.5m, sessionState.Usage.Cost!.Amount);
        }
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenSessionLoadReturnsModeAndConfig_ProjectsVisibleSessionState()
    {
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionLoadResponse(
                modes: new SessionModesState
                {
                    CurrentModeId = "agent",
                    AvailableModes =
                    [
                        new SalmonEgg.Domain.Models.Protocol.SessionMode { Id = "agent", Name = "Agent", Description = "Agent mode" },
                        new SalmonEgg.Domain.Models.Protocol.SessionMode { Id = "plan", Name = "Plan", Description = "Plan mode" }
                    ]
                },
                configOptions: CreateModeConfigOptions("agent")));

        var syncContext = new ImmediateSynchronizationContext();
        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                await sink.SelectProfileAsync(profile, cancellationToken);
                sink.ReplaceChatService(chatService.Object);
                await DispatchConnectedAsync(fixture!, profile.Id);
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });
        commands.Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected non-context ACP connect path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected remote session creation path."));
        commands.Setup(x => x.SendPromptAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.CancelPromptAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        commands.Setup(x => x.DisconnectAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            fixture.ViewModel.ReplaceChatService(chatService.Object);

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
            });
            await DispatchConnectedAsync(fixture, "profile-1");

            var hydrated = await fixture.ViewModel.HydrateActiveConversationAsync();

            Assert.True(hydrated, fixture.ViewModel.ErrorMessage);
            await WaitForConditionAsync(() => Task.FromResult(
                fixture.ViewModel.AvailableModes.Count == 2
                && fixture.ViewModel.ConfigOptions.Count == 1
                && string.Equals(fixture.ViewModel.SelectedMode?.ModeId, "agent", StringComparison.Ordinal)));
            Assert.False(fixture.ViewModel.IsOverlayVisible);
            Assert.Equal(2, fixture.ViewModel.AvailableModes.Count);
            Assert.Equal("agent", fixture.ViewModel.SelectedMode?.ModeId);
            Assert.Single(fixture.ViewModel.ConfigOptions);
            Assert.Equal("mode", fixture.ViewModel.ConfigOptions[0].Id);
            Assert.Equal("agent", fixture.ViewModel.ConfigOptions[0].Value);
            Assert.True(fixture.ViewModel.ShowConfigOptionsPanel);
        }
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenSessionLoadReturnsConfigOptionsWithUnknownType_IgnoresLegacyModesAndUnknownConfigOptions()
    {
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionLoadResponse(
                modes: new SessionModesState
                {
                    CurrentModeId = "legacy-mode",
                    AvailableModes =
                    [
                        new SalmonEgg.Domain.Models.Protocol.SessionMode { Id = "legacy-mode", Name = "Legacy" },
                        new SalmonEgg.Domain.Models.Protocol.SessionMode { Id = "legacy-alt", Name = "Legacy Alt" }
                    ]
                },
                configOptions:
                [
                    new ConfigOption
                    {
                        Id = "mode",
                        Category = "mode",
                        Type = "select",
                        CurrentValue = "config-mode",
                        Options =
                        [
                            new ConfigOptionValue { Value = "config-mode", Name = "Config Mode" },
                            new ConfigOptionValue { Value = "config-alt", Name = "Config Alt" }
                        ]
                    },
                    new ConfigOption
                    {
                        Id = "future-switch",
                        Name = "Future switch",
                        Type = "future-type",
                        CurrentValue = "legacy-mode",
                        Options =
                        [
                            new ConfigOptionValue { Value = "legacy-mode", Name = "Legacy Mode" }
                        ]
                    }
                ]));

        var syncContext = new ImmediateSynchronizationContext();
        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                await sink.SelectProfileAsync(profile, cancellationToken);
                sink.ReplaceChatService(chatService.Object);
                await DispatchConnectedAsync(fixture!, profile.Id);
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });
        commands.Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected non-context ACP connect path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected remote session creation path."));
        commands.Setup(x => x.SendPromptAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.CancelPromptAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        commands.Setup(x => x.DisconnectAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fixture = CreateViewModel(syncContext, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            fixture.ViewModel.ReplaceChatService(chatService.Object);

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
            });
            await DispatchConnectedAsync(fixture, "profile-1");

            var hydrated = await fixture.ViewModel.HydrateActiveConversationAsync();

            Assert.True(hydrated, fixture.ViewModel.ErrorMessage);
            await WaitForConditionAsync(() => Task.FromResult(
                fixture.ViewModel.AvailableModes.Count == 2
                && fixture.ViewModel.ConfigOptions.Count == 1
                && string.Equals(fixture.ViewModel.SelectedMode?.ModeId, "config-mode", StringComparison.Ordinal)));
            Assert.False(fixture.ViewModel.IsOverlayVisible);
            Assert.Equal(2, fixture.ViewModel.AvailableModes.Count);
            Assert.Equal("config-mode", fixture.ViewModel.SelectedMode?.ModeId);
            Assert.Single(fixture.ViewModel.ConfigOptions);
            Assert.Equal("mode", fixture.ViewModel.ConfigOptions[0].Id);
            Assert.Equal("config-mode", fixture.ViewModel.ConfigOptions[0].Value);
            Assert.True(fixture.ViewModel.ShowConfigOptionsPanel);
        }
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_RemoteBoundConversation_WhenSelectedProfileTargetsDifferentAgent_ReconnectsBoundProfileBeforeLoad()
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

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\bound");

        var staleService = CreateConnectedChatService();
        staleService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        staleService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        var reboundService = CreateConnectedChatService();
        reboundService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        reboundService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-1", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                await sink.SelectProfileAsync(profile, cancellationToken);
                await sink.ReplaceChatServiceAsync(reboundService.Object, cancellationToken);
                await DispatchConnectedAsync(fixture!, profile.Id);
                return new AcpTransportApplyResult(reboundService.Object, new InitializeResponse());
            });

        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-2", "Profile 2"));
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-1",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));

            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(staleService.Object));
            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
            });
            await DispatchConnectedAsync(fixture, "profile-2");

            var hydrated = await fixture.ViewModel.HydrateActiveConversationAsync();

            Assert.True(hydrated, fixture.ViewModel.ErrorMessage);
            commands.Verify(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-1", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()), Times.Once);
            staleService.Verify(
                service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()),
                Times.Never);
            reboundService.Verify(
                service => service.LoadSessionAsync(
                    It.Is<SessionLoadParams>(parameters =>
                        string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)
                        && string.Equals(parameters.Cwd, @"C:\repo\bound", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
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
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                await sink.SelectProfileAsync(profile, cancellationToken);
                sink.ReplaceChatService(chatService.Object);
                await DispatchConnectedAsync(fixture!, profile.Id);
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
            await DispatchConnectedAsync(fixture, "profile-1");

            Assert.Equal("conv-1", (await fixture.GetStateAsync()).HydratedConversationId);

            var activated = await fixture.ViewModel.SwitchConversationAsync("conv-2");

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
        await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");

        Assert.Equal("conv-1", (await fixture.GetStateAsync()).HydratedConversationId);

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.IsOverlayVisible
            && IsUserFriendlyHydrationOverlayStatus(fixture.ViewModel.OverlayStatusText)),
            timeoutMilliseconds: 5000);

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
        await DispatchConnectedAsync(fixture, "profile-1");
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
    public async Task HydrateActiveConversationAsync_WhenHydrationAttemptBecomesStale_RestoresCachedProjectionInsteadOfLeavingBlankTranscript()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "cached-1",
                    Timestamp = new DateTime(2026, 3, 1, 0, 0, 1, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "cached local transcript"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 1, DateTimeKind.Utc)));

        var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        ReplayLoadChatService? innerChatService = null;
        AcpChatServiceAdapter? adapter = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = async (_, _) =>
            {
                loadStarted.TrySetResult(null);
                await allowLoadCompletion.Task;
                adapter!.BeginHydrationBufferingScope("remote-1");
                return SessionLoadResponse.Completed;
            }
        };

        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));

        var cachedTranscript = ImmutableList.Create(
            new ConversationMessageSnapshot
            {
                Id = "cached-1",
                Timestamp = new DateTime(2026, 3, 1, 0, 0, 1, DateTimeKind.Utc),
                IsOutgoing = false,
                ContentType = "text",
                TextContent = "cached local transcript"
            });

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty
                .Add("conv-1", new ConversationContentSlice(cachedTranscript, ImmutableList<ConversationPlanEntrySnapshot>.Empty, false, null)),
            Transcript = cachedTranscript
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                fixture.ViewModel.MessageHistory.Any(message =>
                    string.Equals(message.TextContent, "cached local transcript", StringComparison.Ordinal))
                && string.Equals(fixture.ViewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)
                && string.Equals(fixture.ViewModel.CurrentRemoteSessionId, "remote-1", StringComparison.Ordinal));
        });

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();
        while (!loadStarted.Task.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.ViewModel.MessageHistory.Count == 0);
        });

        allowLoadCompletion.TrySetResult(null);
        await syncContext.RunUntilCompletedAsync(hydrationTask);
        var hydrated = await hydrationTask;

        Assert.False(hydrated);
        Assert.Contains(
            fixture.ViewModel.MessageHistory,
            message => string.Equals(message.TextContent, "cached local transcript", StringComparison.Ordinal));
        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);
        Assert.Equal("remote-1", fixture.ViewModel.CurrentRemoteSessionId);
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
        await DispatchConnectedAsync(fixture, "profile-1");
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
            IsHydrating = true,
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "conv-1",
                new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        syncContext.RunAll();
        await Task.Delay(50);
        syncContext.RunAll();

        await WaitForConditionAsync(() => Task.FromResult(
            fixture.ViewModel.IsOverlayVisible
            && IsUserFriendlyHydrationOverlayStatus(fixture.ViewModel.OverlayStatusText)));
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
    public async Task ApplyStoreProjection_WhenSameConversationTranscriptGrowsLarge_ReplacesMessageHistoryCollection()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);

        await syncContext.RunUntilCompletedAsync(fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript = ImmutableList.Create(
                new ConversationMessageSnapshot
                {
                    Id = "message-0",
                    Timestamp = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "seed"
                })
        });
        syncContext.RunAll();

        var originalHistory = fixture.ViewModel.MessageHistory;

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript = ImmutableList.CreateRange(
                Enumerable.Range(0, 96)
                    .Select(index => new ConversationMessageSnapshot
                    {
                        Id = $"message-{index}",
                        Timestamp = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc).AddSeconds(index),
                        IsOutgoing = false,
                        ContentType = "text",
                        TextContent = index == 0 ? "seed" : $"payload-{index}"
                    }))
        });
        syncContext.RunAll();

        Assert.NotSame(originalHistory, fixture.ViewModel.MessageHistory);
        Assert.Equal(96, fixture.ViewModel.MessageHistory.Count);
        Assert.Equal("message-95", fixture.ViewModel.MessageHistory[^1].Id);
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenCompletionModeIsLoadResponse_CompletesBeforeReplayStarts()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.Preferences.IsLoaded);
        });
        fixture.Preferences.AcpHydrationCompletionMode = "LoadResponse";

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
        await DispatchConnectedAsync(fixture, "profile-1");
        syncContext.RunAll();

        var hydrationTask = fixture.ViewModel.HydrateActiveConversationAsync();

        while (!loadReturned.Task.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!hydrationTask.IsCompleted && DateTime.UtcNow < deadline)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        Assert.True(
            hydrationTask.IsCompleted,
            "LoadResponse mode should complete hydration quickly without waiting for replay-start timeout.");
        Assert.True(await hydrationTask);
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
        await DispatchConnectedAsync(fixture, "profile-1");
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

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                fixture.ViewModel.IsOverlayVisible
                && !fixture.ViewModel.MessageHistory.Any(message =>
                    string.Equals(message.TextContent, "late replay after plan", StringComparison.Ordinal)));
        }, timeoutMilliseconds: 4000);

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("late replay after plan"))));

        await syncContext.RunUntilCompletedAsync(hydrationTask);

        Assert.True(await hydrationTask);
        await WaitForConditionAsync(() => Task.FromResult(!fixture.ViewModel.IsOverlayVisible), timeoutMilliseconds: 15000);
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
        await DispatchConnectedAsync(fixture, "profile-1");
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
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(string.Equals(
                fixture.ViewModel.CurrentSessionDisplayName,
                "remote title only",
                StringComparison.Ordinal));
        }, timeoutMilliseconds: 15000);
        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            return fixture.ViewModel.MessageHistory.Any(message =>
                string.Equals(message.TextContent, "late replay after title", StringComparison.Ordinal));
        }, timeoutMilliseconds: 15000);
        await WaitForConditionAsync(() => Task.FromResult(!fixture.ViewModel.IsOverlayVisible), timeoutMilliseconds: 15000);
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
        await DispatchConnectedAsync(fixture, "profile-1");
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
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.ViewModel.MessageHistory.Count > 0);
        }, timeoutMilliseconds: 8000);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(!fixture.ViewModel.IsOverlayVisible);
        }, timeoutMilliseconds: 8000);
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
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "conv-1",
                new ConversationContentSlice(
                    ImmutableList.Create(new ConversationMessageSnapshot
                    {
                        Id = "seed-1",
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "known local prompt",
                        Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
                    }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
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

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                fixture.ViewModel.IsOverlayVisible
                && !fixture.ViewModel.MessageHistory.Any(message =>
                    string.Equals(message.TextContent, "remote history answer", StringComparison.Ordinal)));
        }, timeoutMilliseconds: 4000);

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("remote history answer"))));

        await syncContext.RunUntilCompletedAsync(hydrationTask);
        await Task.Delay(100);
        syncContext.RunAll();

        Assert.True(await hydrationTask);
        await WaitForConditionAsync(() =>
        {
            var combinedText = string.Join(
                "\n",
                fixture.ViewModel.MessageHistory.Select(message => message.TextContent ?? string.Empty));
            return Task.FromResult(combinedText.Contains("remote history answer", StringComparison.Ordinal));
        }, timeoutMilliseconds: 4000);
        await WaitForConditionAsync(() => Task.FromResult(!fixture.ViewModel.IsOverlayVisible), timeoutMilliseconds: 6000);

        var duplicatedPromptCount = fixture.ViewModel.MessageHistory.Count(message =>
            message.IsOutgoing
            && string.Equals(message.TextContent, "known local prompt", StringComparison.Ordinal));
        Assert.Equal(1, duplicatedPromptCount);
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

        var activationTask = fixture.ViewModel.SwitchConversationAsync("conv-1");
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

        await fixture.ViewModel.SwitchConversationAsync("conv-1");

        Assert.False(fixture.ViewModel.IsLayoutLoading);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
        Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
    }

    [Fact]
    public async Task SelectAndHydrateConversationAsync_WhenInvokedOffUiThread_QueuesLayoutLoadingStateChangesOnUiDispatcher()
    {
        var syncContext = new QueueingSynchronizationContext();
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationActivationResult(false, "conv-1", "ActivationFailed"));

        await using var fixture = CreateViewModel(syncContext, conversationActivationCoordinator: activationCoordinator.Object);
        syncContext.RunAll();

        var switchTask = Task.Run(async () => await fixture.ViewModel.SwitchConversationAsync("conv-1"));

        await WaitForConditionAsync(() => Task.FromResult(syncContext.PendingCount > 0));
        await syncContext.RunUntilCompletedAsync(switchTask);

        Assert.False(fixture.ViewModel.IsLayoutLoading);
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
        await fixture.ViewModel.RestoreAsync();
        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-1" });

        await fixture.ViewModel.SwitchConversationAsync("conv-1");

        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);
        Assert.Empty(fixture.ViewModel.MessageHistory);
        Assert.False(fixture.ViewModel.IsLayoutLoading);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
        Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
    }

    [Fact]
    public async Task SelectAndHydrateConversationAsync_WhenActivatedConversationHasProjectedMessagesAndNoPendingHydration_ClearsLayoutLoadingState()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationActivationResult(true, "conv-1", null));

        await using var fixture = CreateViewModel(syncContext, conversationActivationCoordinator: activationCoordinator.Object);
        await fixture.ViewModel.RestoreAsync();
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "projected"
                }
            ]
        });

        await fixture.ViewModel.SwitchConversationAsync("conv-1");

        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);
        Assert.Single(fixture.ViewModel.MessageHistory);
        Assert.False(fixture.ViewModel.IsHydrating);
        Assert.False(fixture.ViewModel.IsRemoteHydrationPending);
        Assert.False(fixture.ViewModel.IsLayoutLoading);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
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
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-2", StringComparison.Ordinal)
                    && context.PreserveConversation
                    && context.ActivationVersion.HasValue),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (_, _, _, _, cancellationToken) =>
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
        await fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-2"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connecting));

        Assert.Equal("conv-remote", fixture.ViewModel.CurrentSessionId);
        Assert.Equal("remote-1", fixture.ViewModel.CurrentRemoteSessionId);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
        Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
    }

    [Fact]
    public async Task Overlay_WhenSessionSwitchingToggles_RaisesPillVisibilityNotificationsImmediately()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-1" });
        var preview = (IConversationActivationPreview)fixture.ViewModel;
        preview.PrimeSessionSwitchPreview("conv-1");

        var raised = new List<string>();
        fixture.ViewModel.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                raised.Add(e.PropertyName!);
            }
        };

        fixture.ViewModel.IsSessionSwitching = true;

        Assert.True(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);
        Assert.Contains(nameof(ChatViewModel.ShouldShowLoadingOverlayStatusPill), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldShowLoadingOverlayPresenter), raised);
        Assert.Contains(nameof(ChatViewModel.OverlayStatusText), raised);
    }

    [Fact]
    public async Task Overlay_WhenOnlyLayoutSettlingIsActive_DoesNotSurfaceActivationPresenter()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        fixture.ViewModel.IsLayoutLoading = true;

        Assert.True(fixture.ViewModel.IsOverlayVisible);
        Assert.False(fixture.ViewModel.IsActivationOverlayVisible);
        Assert.False(fixture.ViewModel.ShouldShowBlockingLoadingMask);
        Assert.False(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);
        Assert.False(fixture.ViewModel.ShouldShowLoadingOverlayPresenter);
        Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);
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
    public async Task Overlay_StatusText_UsesUserFriendlyLanguageWithoutProtocolJargon()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "conv-1" });
        var preview = (IConversationActivationPreview)fixture.ViewModel;
        preview.PrimeSessionSwitchPreview("conv-1");
        fixture.ViewModel.IsSessionSwitching = true;

        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.OverlayStatusText));
        Assert.Contains("切换", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("ACP", fixture.ViewModel.OverlayStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("协议", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);

        fixture.ViewModel.IsSessionSwitching = false;
        await fixture.UpdateStateAsync(state => state with { IsHydrating = true });

        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.OverlayStatusText));
        Assert.Contains("加载", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("ACP", fixture.ViewModel.OverlayStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("协议", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overlay_WhenPreviewingDifferentConversationWithVisibleTranscript_ShowsBlockingMask()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "stale transcript"
                }
            ]
        });

        var preview = (IConversationActivationPreview)fixture.ViewModel;
        preview.PrimeSessionSwitchPreview("conv-2");

        Assert.True(fixture.ViewModel.HasVisibleTranscriptContent);
        Assert.True(fixture.ViewModel.IsOverlayVisible);
        Assert.True(fixture.ViewModel.ShouldShowBlockingLoadingMask);
        Assert.Contains("切换", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overlay_WhenPreviewIsPrimedBeforeChatShellNavigation_BecomesVisibleEvenWhileShellContentIsStart()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Start
        };
        await using var fixture = CreateViewModel(syncContext, shellNavigationRuntimeState: runtimeState);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "stale transcript"
                }
            ]
        });

        var preview = (IConversationActivationPreview)fixture.ViewModel;
        preview.PrimeSessionSwitchPreview("conv-2");

        Assert.Equal(ShellNavigationContent.Start, runtimeState.CurrentShellContent);
        Assert.True(fixture.ViewModel.IsActivationOverlayVisible);
        Assert.True(fixture.ViewModel.ShouldShowBlockingLoadingMask);
        Assert.Contains("切换", fixture.ViewModel.OverlayStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellOverlayProjection_WhenPreviewIsPrimedBeforeChatShellNavigation_BecomesVisibleEvenWhileShellContentIsStart()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Start
        };
        await using var fixture = CreateViewModel(syncContext, shellNavigationRuntimeState: runtimeState);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        var shellOverlay = new ShellSessionActivationOverlayViewModel(fixture.ViewModel, runtimeState);

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "stale transcript"
                }
            ]
        });

        var preview = (IConversationActivationPreview)fixture.ViewModel;
        preview.PrimeSessionSwitchPreview("conv-2");

        Assert.Equal(ShellNavigationContent.Start, runtimeState.CurrentShellContent);
        Assert.True(shellOverlay.IsOverlayVisible);
        Assert.True(shellOverlay.ShowsBlockingMask);
        Assert.True(shellOverlay.ShowsPresenter);
        Assert.Contains("切换", shellOverlay.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellOverlayProjection_WhenSourceOverlayChanges_RaisesProjectionNotifications()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Start
        };
        await using var fixture = CreateViewModel(syncContext, shellNavigationRuntimeState: runtimeState);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        var shellOverlay = new ShellSessionActivationOverlayViewModel(fixture.ViewModel, runtimeState);
        var raised = new List<string>();
        shellOverlay.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                raised.Add(e.PropertyName!);
            }
        };

        var preview = (IConversationActivationPreview)fixture.ViewModel;
        preview.PrimeSessionSwitchPreview("conv-2");

        Assert.Contains(nameof(ShellSessionActivationOverlayViewModel.IsOverlayVisible), raised);
        Assert.Contains(nameof(ShellSessionActivationOverlayViewModel.ShowsBlockingMask), raised);
        Assert.Contains(nameof(ShellSessionActivationOverlayViewModel.ShowsPresenter), raised);
        Assert.Contains(nameof(ShellSessionActivationOverlayViewModel.StatusText), raised);
    }

    [Fact]
    public async Task ShellOverlayProjection_WhenChatViewModelRaisesOverlayPropertyOffUiThread_MarshalsProjectionNotificationsToUiDispatcher()
    {
        var syncContext = new QueueingSynchronizationContext();
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Chat
        };
        await using var fixture = CreateViewModel(syncContext, shellNavigationRuntimeState: runtimeState);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        var shellOverlay = new ShellSessionActivationOverlayViewModel(fixture.ViewModel, runtimeState);
        var notificationCompleted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedOnUiThread = true;

        shellOverlay.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ShellSessionActivationOverlayViewModel.StatusText))
            {
                return;
            }

            observedOnUiThread = syncContext.HasThreadAccess;
            notificationCompleted.TrySetResult(null);
        };

        var raisePropertyChanged = typeof(CommunityToolkit.Mvvm.ComponentModel.ObservableObject)
            .GetMethod("OnPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(string)], null);
        Assert.NotNull(raisePropertyChanged);

        await Task.Run(() => raisePropertyChanged!.Invoke(fixture.ViewModel, [nameof(ChatViewModel.OverlayStatusText)]));

        Assert.False(notificationCompleted.Task.IsCompleted);

        syncContext.RunAll();
        await notificationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(observedOnUiThread);
    }

    [Fact]
    public async Task Overlay_WhenCurrentSessionChangesBeforeTranscriptReplacement_KeepsBlockingMaskForStaleVisibleTranscript()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "stale transcript"
                }
            ]
        });

        var preview = (IConversationActivationPreview)fixture.ViewModel;
        preview.PrimeSessionSwitchPreview("conv-2");

        bool? blockingMaskWhileTranscriptWasStale = null;
        fixture.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId)
                && string.Equals(fixture.ViewModel.CurrentSessionId, "conv-2", StringComparison.Ordinal)
                && fixture.ViewModel.MessageHistory.Any(message =>
                    string.Equals(message.TextContent, "stale transcript", StringComparison.Ordinal)))
            {
                blockingMaskWhileTranscriptWasStale = fixture.ViewModel.ShouldShowBlockingLoadingMask;
            }
        };

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-2",
            Transcript = [],
            IsHydrating = true
        });

        Assert.True(
            blockingMaskWhileTranscriptWasStale,
            "Blocking loading mask must remain visible while the new CurrentSessionId points at conv-2 but MessageHistory still contains the stale conv-1 transcript.");
    }

    [Fact]
    public async Task VisibleConversationContent_WhenTranscriptOwnerIsStale_HidesHeaderTranscriptAndInputUntilProjectionCatchesUp()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "stale transcript"
                }
            ]
        });

        var preview = (IConversationActivationPreview)fixture.ViewModel;
        preview.PrimeSessionSwitchPreview("conv-2");

        (bool header, bool transcript, bool input)? visibleStateWhileTranscriptWasStale = null;
        fixture.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId)
                && string.Equals(fixture.ViewModel.CurrentSessionId, "conv-2", StringComparison.Ordinal)
                && fixture.ViewModel.MessageHistory.Any(message =>
                    string.Equals(message.TextContent, "stale transcript", StringComparison.Ordinal)))
            {
                visibleStateWhileTranscriptWasStale = (
                    fixture.ViewModel.ShouldShowSessionHeader,
                    fixture.ViewModel.ShouldShowTranscriptSurface,
                    fixture.ViewModel.ShouldShowConversationInputSurface);
            }
        };

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-2",
            Transcript = [],
            IsHydrating = true
        });

        Assert.NotNull(visibleStateWhileTranscriptWasStale);
        Assert.False(visibleStateWhileTranscriptWasStale.Value.header);
        Assert.False(visibleStateWhileTranscriptWasStale.Value.transcript);
        Assert.False(visibleStateWhileTranscriptWasStale.Value.input);
    }

    [Fact]
    public async Task VisibleConversationContent_WhenShellHasNewLatestIntent_HidesHeaderTranscriptAndInputBeforeConversationSwitchCommits()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var runtimeState = new ShellNavigationRuntimeStateStore();
        await using var fixture = CreateViewModel(syncContext, shellNavigationRuntimeState: runtimeState);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "restored transcript"
                }
            ]
        });

        Assert.True(fixture.ViewModel.ShouldShowSessionHeader);
        Assert.True(fixture.ViewModel.ShouldShowTranscriptSurface);
        Assert.True(fixture.ViewModel.ShouldShowConversationInputSurface);
        Assert.True(fixture.ViewModel.ShouldShowActiveConversationRoot);
        Assert.True(fixture.ViewModel.ShouldLoadActiveConversationRoot);

        runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
            "conv-2",
            null,
            Version: 7,
            SessionActivationPhase.NavigatingToChatShell);
        runtimeState.DesiredSessionId = "conv-2";
        runtimeState.IsSessionActivationInProgress = true;

        Assert.False(fixture.ViewModel.ShouldShowActiveConversationRoot);
        Assert.False(fixture.ViewModel.ShouldLoadActiveConversationRoot);
        Assert.False(fixture.ViewModel.ShouldShowSessionHeader);
        Assert.False(fixture.ViewModel.ShouldShowTranscriptSurface);
        Assert.False(fixture.ViewModel.ShouldLoadTranscriptSurface);
        Assert.False(fixture.ViewModel.ShouldShowConversationInputSurface);
    }

    [Fact]
    public async Task VisibleConversationContent_WhenShellLatestIntentChanges_RaisesDerivedVisibilityNotifications()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var runtimeState = new ShellNavigationRuntimeStateStore();
        await using var fixture = CreateViewModel(syncContext, shellNavigationRuntimeState: runtimeState);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "restored transcript"
                }
            ]
        });

        var raised = new List<string>();
        fixture.ViewModel.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                raised.Add(e.PropertyName!);
            }
        };

        runtimeState.DesiredSessionId = "conv-2";
        runtimeState.IsSessionActivationInProgress = true;

        Assert.Contains(nameof(ChatViewModel.ShouldShowSessionHeader), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldShowTranscriptSurface), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldLoadTranscriptSurface), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldShowConversationInputSurface), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldShowActiveConversationRoot), raised);
        Assert.Contains(nameof(ChatViewModel.ShouldLoadActiveConversationRoot), raised);
    }

    [Fact]
    public async Task VisibleConversationContent_WhenCurrentSessionOwnsVisibleState_KeepsHeaderAndInputVisible()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript = [],
            IsHydrating = false
        });
        fixture.ViewModel.IsSessionActive = true;

        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);
        Assert.False(fixture.ViewModel.ShouldShowBlockingLoadingMask);
        Assert.True(fixture.ViewModel.ShouldShowSessionHeader);
        Assert.False(fixture.ViewModel.ShouldShowTranscriptSurface);
        Assert.True(fixture.ViewModel.ShouldShowConversationInputSurface);
    }

    [Fact]
    public async Task Overlay_WhenVisible_DisablesPromptInputUntilActivationSettles()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript = [],
            IsHydrating = false
        });
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)));

        var chatService = new Mock<IChatService>();
        chatService.SetupGet(service => service.IsConnected).Returns(true);
        chatService.SetupGet(service => service.IsInitialized).Returns(true);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        fixture.ViewModel.IsSessionActive = true;
        fixture.ViewModel.IsConnected = true;
        fixture.ViewModel.CurrentPrompt = "hello";

        Assert.True(fixture.ViewModel.IsInputEnabled);

        var raised = new List<string>();
        fixture.ViewModel.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                raised.Add(e.PropertyName!);
            }
        };

        var preview = (IConversationActivationPreview)fixture.ViewModel;
        preview.PrimeSessionSwitchPreview("conv-2");

        Assert.True(fixture.ViewModel.IsOverlayVisible);
        Assert.False(fixture.ViewModel.CanSendPromptUi);
        Assert.False(fixture.ViewModel.IsInputEnabled);
        Assert.Contains(nameof(ChatViewModel.CanSendPromptUi), raised);
        Assert.Contains(nameof(ChatViewModel.IsInputEnabled), raised);
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
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");

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
    public async Task ConversationSessionSwitcherContract_RemoteBoundConversation_DoesNotRegressOverlayStatusBackToPreparingAfterHydrationStarts()
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
        await DispatchConnectedAsync(fixture, "profile-1");
        syncContext.RunAll();

        var observedStatuses = new List<string>();
        void RecordStatus()
        {
            if (!string.IsNullOrWhiteSpace(fixture.ViewModel.OverlayStatusText))
            {
                observedStatuses.Add(fixture.ViewModel.OverlayStatusText);
            }
        }

        fixture.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatViewModel.OverlayStatusText))
            {
                RecordStatus();
            }
        };

        var switcher = (IConversationSessionSwitcher)fixture.ViewModel;
        var activationTask = switcher.SwitchConversationAsync("conv-2");

        while (!loadReturned.Task.IsCompleted || !activationTask.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        Assert.True(await activationTask);
        Assert.True(IsUserFriendlyHydrationOverlayStatus(fixture.ViewModel.OverlayStatusText));
        RecordStatus();

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-2",
            new AgentMessageUpdate(new TextContentBlock("replayed message"))));

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                fixture.ViewModel.MessageHistory.Any(message =>
                    string.Equals(message.TextContent, "replayed message", StringComparison.Ordinal)));
        });

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(!fixture.ViewModel.IsOverlayVisible);
        }, timeoutMilliseconds: 7000);

        static bool IsHydrationLifecycleStatus(string status) =>
            status.Contains("加载", StringComparison.Ordinal)
            || status.Contains("打开", StringComparison.Ordinal)
            || status.Contains("获取", StringComparison.Ordinal)
            || status.Contains("同步", StringComparison.Ordinal)
            || status.Contains("整理", StringComparison.Ordinal)
            || status.Contains("完成", StringComparison.Ordinal);

        var hydrationStatusIndex = observedStatuses.FindIndex(IsHydrationLifecycleStatus);
        Assert.True(hydrationStatusIndex >= 0, $"Observed status sequence never entered hydration: [{string.Join(" | ", observedStatuses)}]");

        var regressedToPreparing = observedStatuses
            .Skip(hydrationStatusIndex + 1)
            .Any(status => status.Contains("切换", StringComparison.Ordinal));
        Assert.False(
            regressedToPreparing,
            $"Overlay status regressed to session-switch preparation after hydration started. Sequence=[{string.Join(" | ", observedStatuses)}]");
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
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
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
        var cachedSnapshot = fixture.Workspace.GetConversationSnapshot("conv-2");
        Assert.NotNull(cachedSnapshot);
        Assert.Contains(
            cachedSnapshot!.Transcript,
            message => string.Equals(message.TextContent, "cached first message", StringComparison.Ordinal));
        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            var content = state.ResolveContentSlice("conv-2");
            syncContext.RunAll();
            return content.HasValue
                && content.Value.Transcript.Any(message =>
                    string.Equals(message.TextContent, "cached first message", StringComparison.Ordinal));
        }, timeoutMilliseconds: 8000);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                loadStarted.Task.IsCompleted
                && !fixture.ViewModel.IsOverlayVisible
                && fixture.ViewModel.MessageHistory.Any(message =>
                    string.Equals(message.TextContent, "cached first message", StringComparison.Ordinal)));
        }, timeoutMilliseconds: 8000);
        Assert.Contains(
            fixture.ViewModel.MessageHistory,
            message => string.Equals(message.TextContent, "cached first message", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SwitchConversationAsync_RemoteBoundConversation_DoesNotReadPreviewStoreOrFlashPreviewTranscript()
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

        var previewStore = new Mock<IConversationPreviewStore>();
        previewStore.Setup(store => store.LoadAsync("conv-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationPreviewSnapshot(
                "conv-2",
                [
                    new PreviewEntry(
                        "assistant",
                        "preview first message",
                        new DateTimeOffset(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)))
                ],
                new DateTimeOffset(new DateTime(2026, 3, 2, 0, 1, 0, DateTimeKind.Utc))));
        previewStore.Setup(store => store.SaveAsync(It.IsAny<ConversationPreviewSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        previewStore.Setup(store => store.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            previewStore: previewStore.Object);
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
        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = async (_, _) =>
            {
                loadStarted.TrySetResult(null);
                await allowLoadCompletion.Task;
                innerChatService!.RaiseSessionUpdate(new SessionUpdateEventArgs(
                    "remote-2",
                    new AgentMessageUpdate(new TextContentBlock("remote replay message"))));
                return SessionLoadResponse.Completed;
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
        await DispatchConnectedAsync(fixture, "profile-1");
        syncContext.RunAll();

        var switchTask = fixture.ViewModel.SwitchConversationAsync("conv-2");

        while (!loadStarted.Task.IsCompleted && !switchTask.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        Assert.True(loadStarted.Task.IsCompleted, "Remote session/load should start for the active remote intent.");
        syncContext.RunAll();

        Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
        Assert.True(fixture.ViewModel.IsOverlayVisible);
        Assert.DoesNotContain(
            fixture.ViewModel.MessageHistory,
            message => string.Equals(message.TextContent, "preview first message", StringComparison.Ordinal));
        Assert.DoesNotContain(
            fixture.ViewModel.MessageHistory,
            message => string.Equals(message.TextContent, "cached first message", StringComparison.Ordinal));

        allowLoadCompletion.TrySetResult(null);

        await syncContext.RunUntilCompletedAsync(switchTask);
        var switched = await switchTask;

        Assert.True(switched);
        previewStore.Verify(store => store.LoadAsync("conv-2", It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(
            fixture.ViewModel.MessageHistory,
            message => string.Equals(message.TextContent, "remote replay message", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyStoreProjection_WhenOnlyConnectionStateChanges_DoesNotResaveUnchangedTranscriptPreview()
    {
        var previewStore = new Mock<IConversationPreviewStore>();
        previewStore.Setup(store => store.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationPreviewSnapshot?)null);
        previewStore.Setup(store => store.SaveAsync(It.IsAny<ConversationPreviewSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        previewStore.Setup(store => store.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var fixture = CreateViewModel(previewStore: previewStore.Object);
        await fixture.ViewModel.RestoreAsync();

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hello"
                }
            ]
        });

        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

        previewStore.Verify(
            store => store.SaveAsync(
                It.Is<ConversationPreviewSnapshot>(snapshot => string.Equals(snapshot.ConversationId, "conv-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenShellRuntimeStatePresent_DoesNotMutateShellActivationOwner()
    {
        var syncContext = new QueueingSynchronizationContext();
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Chat,
            LatestActivationToken = 41,
            DesiredSessionId = "existing-owner",
            CommittedSessionId = "conv-local",
            ActiveSessionActivationVersion = 41,
            CommittedSessionActivationVersion = 40,
            IsSessionActivationInProgress = true,
            ActiveSessionActivation = new SessionActivationSnapshot(
                "existing-owner",
                "project-existing",
                41,
                SessionActivationPhase.RemoteHydrationPending)
        };
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

        var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ReplayLoadChatService? innerChatService = null;
        innerChatService = new ReplayLoadChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = async (_, _) =>
            {
                loadStarted.TrySetResult(null);
                await allowLoadCompletion.Task;
                innerChatService!.RaiseSessionUpdate(new SessionUpdateEventArgs(
                    "remote-1",
                    new AgentMessageUpdate(new TextContentBlock("runtime-state replay message"))));
                return SessionLoadResponse.Completed;
            }
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);

        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            shellNavigationRuntimeState: runtimeState);
        await syncContext.RunUntilCompletedAsync(fixture.ViewModel.RestoreAsync());
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript: [],
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
                .Add("conv-local", new ConversationBindingSlice("conv-local", "remote-local", "profile-1"))
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        syncContext.RunAll();

        var switchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote");

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                loadStarted.Task.IsCompleted
                && runtimeState.ActiveSessionActivation is { SessionId: "existing-owner", Phase: SessionActivationPhase.RemoteHydrationPending }
                && runtimeState.IsSessionActivationInProgress
                && runtimeState.LatestActivationToken == 41
                && string.Equals(runtimeState.DesiredSessionId, "existing-owner", StringComparison.Ordinal));
        });

        allowLoadCompletion.TrySetResult(null);
        await syncContext.RunUntilCompletedAsync(switchTask);

        Assert.True(await switchTask);
        Assert.Equal("conv-local", runtimeState.CommittedSessionId);
        Assert.Equal("existing-owner", runtimeState.ActiveSessionActivation?.SessionId);
        Assert.Equal(SessionActivationPhase.RemoteHydrationPending, runtimeState.ActiveSessionActivation?.Phase);
        Assert.True(runtimeState.IsSessionActivationInProgress);
        Assert.Equal(41, runtimeState.LatestActivationToken);
        Assert.Equal(41, runtimeState.ActiveSessionActivationVersion);
        Assert.Equal(40, runtimeState.CommittedSessionActivationVersion);
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
        await DispatchConnectedAsync(fixture, "profile-1");
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
        var overlayVisible = false;
        var overlayWaitDeadlineUtc = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < overlayWaitDeadlineUtc)
        {
            syncContext.RunAll();
            overlayVisible =
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-2", StringComparison.Ordinal)
                && fixture.ViewModel.IsOverlayVisible
                && IsUserFriendlyHydrationOverlayStatus(fixture.ViewModel.OverlayStatusText);
            if (overlayVisible)
            {
                break;
            }

            await Task.Delay(20);
        }

        Assert.True(
            overlayVisible,
            $"Expected remote switch overlay to remain visible until replay starts. CurrentSessionId={fixture.ViewModel.CurrentSessionId ?? "<null>"} IsOverlayVisible={fixture.ViewModel.IsOverlayVisible} OverlayStatusText='{fixture.ViewModel.OverlayStatusText}' IsHydrating={fixture.ViewModel.IsHydrating} IsRemoteHydrationPending={fixture.ViewModel.IsRemoteHydrationPending} IsSessionSwitching={fixture.ViewModel.IsSessionSwitching} SessionSwitchOwner={GetPrivateFieldValue<string>(fixture.ViewModel, "_sessionSwitchOverlayConversationId") ?? "<null>"} HistoryOwner={GetPrivateFieldValue<string>(fixture.ViewModel, "_historyOverlayConversationId") ?? "<null>"} PendingHistoryDismiss={GetPrivateFieldValue<string>(fixture.ViewModel, "_pendingHistoryOverlayDismissConversationId") ?? "<null>"} HydrationPhase={GetPrivateFieldValue<object>(fixture.ViewModel, "_hydrationOverlayPhase")?.ToString() ?? "<null>"}.");

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-2",
            new AgentMessageUpdate(new TextContentBlock("late replay"))));

        var lateReplayProjected = false;
        var lateReplayDeadlineUtc = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < lateReplayDeadlineUtc)
        {
            syncContext.RunAll();
            lateReplayProjected = fixture.ViewModel.MessageHistory.Any(message =>
                string.Equals(message.TextContent, "late replay", StringComparison.Ordinal));
            if (lateReplayProjected)
            {
                break;
            }

            await Task.Delay(20);
        }

        var lateReplayState = await fixture.GetStateAsync();
        Assert.True(
            lateReplayProjected,
            $"Expected late replay to project into the switched conversation. CurrentSessionId={fixture.ViewModel.CurrentSessionId ?? "<null>"} HydratedConversationId={lateReplayState.HydratedConversationId ?? "<null>"} TranscriptCount={fixture.ViewModel.MessageHistory.Count} IsOverlayVisible={fixture.ViewModel.IsOverlayVisible} OverlayStatusText='{fixture.ViewModel.OverlayStatusText}' RuntimePhase={lateReplayState.ResolveRuntimeState("conv-2")?.Phase.ToString() ?? "<null>"} BoundRemoteSessionId={lateReplayState.ResolveBinding("conv-2")?.RemoteSessionId ?? "<null>"} PendingHistoryDismiss={GetPrivateFieldValue<string>(fixture.ViewModel, "_pendingHistoryOverlayDismissConversationId") ?? "<null>"}.");

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
        var secondRemoteLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
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

                    secondRemoteLoadStarted.TrySetResult(null);

                    return SessionLoadResponse.Completed;
                }

                throw new InvalidOperationException($"Unexpected session load: {parameters.SessionId}");
            });
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
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
        }, timeoutMilliseconds: 10000);

        var duplicateActivation = switcher.SwitchConversationAsync("conv-1");
        while (!duplicateActivation.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        Assert.True(await duplicateActivation);
        syncContext.RunAll();
        Assert.Equal(1, Volatile.Read(ref remoteOneLoadCalls));

        var laterActivation = switcher.SwitchConversationAsync("conv-2");
        while (!laterActivation.IsCompleted)
        {
            if (!syncContext.RunNext())
            {
                await Task.Delay(10);
            }
        }

        Assert.True(await laterActivation);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(secondRemoteLoadStarted.Task.IsCompleted);
        }, timeoutMilliseconds: 30000);

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
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
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
        }, timeoutMilliseconds: 8000);

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
        }, timeoutMilliseconds: 2000);

        allowFirstLoadCompletion.TrySetResult(null);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(!fixture.ViewModel.IsOverlayVisible);
        }, timeoutMilliseconds: 8000);
    }

    [Fact]
    public async Task ConversationSessionSwitcherContract_WhenEarlierRemoteLoadIgnoresCancellation_LaterSelectionStillStartsLatestRemoteLoad()
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
            .Returns<SessionLoadParams, CancellationToken>(async (parameters, _) =>
            {
                if (string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal))
                {
                    firstLoadStarted.TrySetResult(null);
                    await allowFirstLoadCompletion.Task;
                    return SessionLoadResponse.Completed;
                }

                if (string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal))
                {
                    secondLoadStarted.TrySetResult(null);
                    return SessionLoadResponse.Completed;
                }

                throw new InvalidOperationException($"Unexpected session load: {parameters.SessionId}");
            });
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
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
        }, timeoutMilliseconds: 8000);

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
        }, timeoutMilliseconds: 2000);

        allowFirstLoadCompletion.TrySetResult(null);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(!fixture.ViewModel.IsOverlayVisible);
        }, timeoutMilliseconds: 8000);
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
                var activeFixture = fixture ?? throw new InvalidOperationException("Fixture must be initialized before connect callback runs.");
                await allowConnectCompletion.Task.WaitAsync(cancellationToken);
                await sink.SelectProfileAsync(profile, cancellationToken);
                sink.ReplaceChatService(chatService.Object);
                await activeFixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
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

            allowConnectCompletion.TrySetResult(null);
            await syncContext.RunUntilCompletedAsync(remoteSwitchTask);

            Assert.False(await remoteSwitchTask);
            syncContext.RunAll();

            var finalState = await fixture.GetStateAsync();
            Assert.Equal("conv-local", finalState.HydratedConversationId);
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
                var activeFixture = fixture ?? throw new InvalidOperationException("Fixture must be initialized before connect callback runs.");
                await allowConnectCompletion.Task.WaitAsync(cancellationToken);
                await sink.SelectProfileAsync(profile, cancellationToken);
                sink.ReplaceChatService(chatService.Object);
                await activeFixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
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
                return Task.FromResult(!fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);
            });

            Assert.False(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);
            Assert.Equal(string.Empty, fixture.ViewModel.OverlayStatusText);

            allowConnectCompletion.TrySetResult(null);
            await syncContext.RunUntilCompletedAsync(remoteSwitchTask);
        }
    }

    [Fact]
    public async Task ActivateConversationAsync_RemoteBoundConversation_WhenCurrentSessionMatchesButRuntimeNotWarm_StillLoadsRemoteSession()
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

        var loadedRemoteSessionId = "remote-2";
        var loadInvocationCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.SetupGet(service => service.CurrentSessionId).Returns(() => loadedRemoteSessionId);
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Callback<SessionLoadParams, CancellationToken>((parameters, _) =>
            {
                loadInvocationCount++;
                loadedRemoteSessionId = parameters.SessionId;
            })
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
                await sink.SelectProfileAsync(profile, cancellationToken);
                await sink.ReplaceChatServiceAsync(chatService.Object, cancellationToken);
                await DispatchConnectedAsync(fixture!, profile.Id);
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

            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", null, "profile-1"))
                    .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
            });
            await DispatchConnectedAsync(fixture, "profile-1");

            var activated = await fixture.ViewModel.SwitchConversationAsync("conv-2");

            Assert.True(activated, fixture.ViewModel.ErrorMessage);
            Assert.Equal(1, loadInvocationCount);
        }
    }

    [Fact]
    public async Task ActivateConversationAsync_RemoteBoundConversation_WhenCurrentSessionMatchesAndRuntimeIsWarm_SkipsReload()
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

        var loadInvocationCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Callback(() => loadInvocationCount++)
            .ReturnsAsync(SessionLoadResponse.Completed);

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "msg-1",
                    Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "warm transcript"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                .Add("conv-1", new ConversationRuntimeSlice(
                    "conv-1",
                    ConversationRuntimePhase.Warm,
                    "conn-1",
                    "remote-1",
                    "profile-1",
                    "SessionLoadCompleted",
                    DateTime.UtcNow))
        });
        await WaitForConditionAsync(() =>
            Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-1", StringComparison.Ordinal)
                && string.Equals(fixture.ViewModel.CurrentRemoteSessionId, "remote-1", StringComparison.Ordinal)));
        var loadCountBeforeActivation = loadInvocationCount;

        var activated = await fixture.ViewModel.SwitchConversationAsync("conv-1");

        Assert.True(activated);
        Assert.Equal(loadCountBeforeActivation, loadInvocationCount);
        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);
        Assert.False(fixture.ViewModel.IsOverlayVisible);
    }

    [Fact]
    public async Task ChatShellViewModel_SelectedMiniWindowSession_WhenRemoteConversationSelected_ShowsLoadingStatusPill()
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
                var activeFixture = fixture ?? throw new InvalidOperationException("Fixture must be initialized before connect callback runs.");
                await allowConnectCompletion.Task.WaitAsync(cancellationToken);
                await sink.SelectProfileAsync(profile, cancellationToken);
                sink.ReplaceChatService(chatService.Object);
                await activeFixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                return new AcpTransportApplyResult(chatService.Object, new InitializeResponse());
            });

        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            var catalog = new ConversationCatalogPresenter();
            var resolver = new Mock<IProjectAffinityResolver>(MockBehavior.Strict);
            var navigationCoordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
            navigationCoordinator.Setup(x => x.ActivateSessionAsync(
                    It.Is<string>(conversationId => string.Equals(conversationId, "conv-remote", StringComparison.Ordinal)),
                    It.IsAny<string?>()))
                .Returns<string, string?>((conversationId, _) => fixture.ViewModel.SwitchConversationAsync(conversationId));

            using var shellLayout = CreateShellLayoutViewModel();
            await using var displayCatalog = CreateDisplayCatalogPresenter(catalog, fixture.ViewModel.Dispatcher);
            var shellViewModel = new ChatShellViewModel(
                fixture.ViewModel,
                shellLayout,
                navigationCoordinator.Object,
                displayCatalog.Presenter,
                resolver.Object,
                fixture.Preferences,
                Mock.Of<ILogger<ChatShellViewModel>>());

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

            var remoteItem = fixture.ViewModel.MiniWindowSessions.Single(item =>
                string.Equals(item.ConversationId, "conv-remote", StringComparison.Ordinal));
            shellViewModel.SelectedMiniWindowSession = remoteItem;

            await WaitForConditionAsync(() =>
            {
                syncContext.RunAll();
                return Task.FromResult(
                    fixture.ViewModel.ShouldShowLoadingOverlayStatusPill
                    || string.Equals(fixture.ViewModel.CurrentSessionId, "conv-remote", StringComparison.Ordinal));
            });

            Assert.True(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);
            Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.OverlayStatusText));
            navigationCoordinator.Verify(x => x.ActivateSessionAsync("conv-remote", It.IsAny<string?>()), Times.Once);
            resolver.Verify(x => x.Resolve(It.IsAny<ProjectAffinityRequest>()), Times.Never);

            allowConnectCompletion.TrySetResult(null);
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
        await DispatchConnectedAsync(fixture, "profile-1");
        syncContext.RunAll();

        var firstRemoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(firstLoadStarted.Task.IsCompleted);
        }, timeoutMilliseconds: 8000);

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
        }, timeoutMilliseconds: 4000);

        Assert.True(await localSwitchTask);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-local", StringComparison.Ordinal)
                && !fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);
        }, timeoutMilliseconds: 4000);

        var secondRemoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await syncContext.RunUntilCompletedAsync(secondRemoteSwitchTask);
        Assert.True(await secondRemoteSwitchTask);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            var replayProjected = fixture.ViewModel.MessageHistory.Any(message =>
                message.TextContent?.Contains("remote replay chunk before load response", StringComparison.Ordinal) == true);
            return Task.FromResult(replayProjected && !fixture.ViewModel.IsOverlayVisible);
        }, timeoutMilliseconds: 15000);

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
        await DispatchConnectedAsync(fixture, "profile-1");
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
        await DispatchConnectedAsync(fixture, "profile-1");
        syncContext.RunAll();

        var firstRemoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote-1");
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(remote1Started.Task.IsCompleted);
        }, timeoutMilliseconds: 8000);

        var secondRemoteSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote-2");
        await syncContext.RunUntilCompletedAsync(secondRemoteSwitchTask);
        Assert.True(await secondRemoteSwitchTask);
        var stateAfterSecondSelection = await fixture.GetStateAsync();
        Assert.Equal("conv-remote-2", stateAfterSecondSelection.HydratedConversationId);
        syncContext.RunAll();
        Assert.Equal("conv-remote-2", fixture.ViewModel.CurrentSessionId);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(remote1Canceled.Task.IsCompleted);
        }, timeoutMilliseconds: 8000);
        await syncContext.RunUntilCompletedAsync(firstRemoteSwitchTask);
        Assert.False(await firstRemoteSwitchTask);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(string.Equals(
                fixture.ViewModel.CurrentSessionId,
                "conv-remote-2",
                StringComparison.Ordinal));
        }, timeoutMilliseconds: 30000);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.ViewModel.MessageHistory.Any(message =>
                message.TextContent?.Contains("fresh remote-2 replay", StringComparison.Ordinal) == true));
        }, timeoutMilliseconds: 30000);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(!fixture.ViewModel.IsOverlayVisible);
        }, timeoutMilliseconds: 30000);

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
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                await sink.SelectProfileAsync(profile, cancellationToken);
                sink.ReplaceChatService(chatService.Object);
                await DispatchConnectedAsync(fixture!, profile.Id);
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
    public async Task SwitchConversationAsync_RemoteBoundConversation_WhenActivationSyncsProfileSelection_ReconnectsBeforeHydrating()
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

        var staleService = CreateConnectedChatService();
        staleService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        staleService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        var reboundService = CreateConnectedChatService();
        reboundService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        reboundService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-remote", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                await sink.SelectProfileAsync(profile, cancellationToken);
                await sink.ReplaceChatServiceAsync(reboundService.Object, cancellationToken);
                await DispatchConnectedAsync(fixture!, profile.Id);
                return new AcpTransportApplyResult(reboundService.Object, new InitializeResponse());
            });

        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            fixture.Profiles.Profiles.Add(new ServerConfiguration { Id = "profile-2", Name = "Profile 2", Transport = TransportType.Stdio });
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

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

            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(staleService.Object));
            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-local",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
            });
            await DispatchConnectedAsync(fixture, "profile-2");

            var switchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote");
            await AwaitWithSynchronizationContextAsync(syncContext, switchTask);
            var switched = await switchTask;

            Assert.True(switched, fixture.ViewModel.ErrorMessage);
            commands.Verify(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-remote", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()), Times.Once);
            staleService.Verify(
                service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()),
                Times.Never);
            reboundService.Verify(
                service => service.LoadSessionAsync(
                    It.Is<SessionLoadParams>(parameters =>
                        string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)
                        && string.Equals(parameters.Cwd, @"C:\repo\remote", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenStaleRemoteConnectCompletesLate_DoesNotReplaceCurrentIntentChatService()
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
        await sessionManager.Object.CreateSessionAsync("conv-remote-1", @"C:\repo\remote-1");

        var staleConnectStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowStaleConnectCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleService = CreateConnectedChatService();
        staleService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-remote-1", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (_, _, sink, _, _) =>
            {
                staleConnectStarted.TrySetResult(null);
                await allowStaleConnectCompletion.Task;

                sink.ReplaceChatService(staleService.Object);
                sink.UpdateAgentIdentity("stale-agent", "1.0.0");
                await DispatchConnectedAsync(fixture!, "profile-1");

                return new AcpTransportApplyResult(
                    staleService.Object,
                    new InitializeResponse(1, new AgentInfo("stale-agent", "1.0.0"), new AgentCapabilities(loadSession: true)));
            });

        commands.Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected non-context ACP connect path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.ApplyTransportConfigurationAsync(
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected transport apply path."));
        commands.Setup(x => x.EnsureRemoteSessionAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected remote session creation path."));
        commands.Setup(x => x.SendPromptAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.DispatchPromptToRemoteSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Xunit.Sdk.XunitException("Unexpected prompt dispatch path."));
        commands.Setup(x => x.CancelPromptAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        commands.Setup(x => x.DisconnectAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
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

            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-local",
                Transcript =
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
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-remote-1", new ConversationBindingSlice("conv-remote-1", "remote-1", "profile-1"))
            });
            syncContext.RunAll();

            var staleSwitchTask = fixture.ViewModel.SwitchConversationAsync("conv-remote-1");
            await WaitForConditionAsync(() =>
            {
                syncContext.RunAll();
                return Task.FromResult(staleConnectStarted.Task.IsCompleted);
            }, timeoutMilliseconds: 8000);

            var returnToLocalTask = fixture.ViewModel.SwitchConversationAsync("conv-local");
            allowStaleConnectCompletion.TrySetResult(null);

            await syncContext.RunUntilCompletedAsync(returnToLocalTask);
            await syncContext.RunUntilCompletedAsync(staleSwitchTask);

            Assert.True(await returnToLocalTask);
            Assert.False(await staleSwitchTask);
            await WaitForConditionAsync(async () =>
            {
                syncContext.RunAll();
                return string.Equals(fixture.ViewModel.CurrentSessionId, "conv-local", StringComparison.Ordinal)
                    && fixture.ViewModel.CurrentChatService is null
                    && fixture.ViewModel.AgentName is null
                    && fixture.ViewModel.AgentVersion is null;
            }, timeoutMilliseconds: 15000);
            Assert.Single(fixture.ViewModel.MessageHistory);
            Assert.Contains("local seed", fixture.ViewModel.MessageHistory[0].TextContent, StringComparison.Ordinal);
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

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");

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
    public async Task SwitchConversationAsync_RemoteBoundConversation_WhenRemoteSessionMissing_ClearsStaleBinding()
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
            .ThrowsAsync(new AcpException(JsonRpcErrorCode.ResourceNotFound, "Resource not found: remote-2"));

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
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "cached-1",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 1, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "cached local transcript"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");

        var switched = await fixture.ViewModel.SwitchConversationAsync("conv-2");

        Assert.True(switched);
        await WaitForConditionAsync(async () =>
        {
            var workspaceBinding = fixture.Workspace.GetRemoteBinding("conv-2");
            var state = await fixture.GetStateAsync();
            return (fixture.ViewModel.ErrorMessage?.Contains("Resource not found", StringComparison.Ordinal) ?? false)
                && fixture.ViewModel.MessageHistory.Any(message =>
                    string.Equals(message.TextContent, "cached local transcript", StringComparison.Ordinal))
                && workspaceBinding is { RemoteSessionId: null, BoundProfileId: "profile-1" }
                && state.ResolveBinding("conv-2") == new ConversationBindingSlice("conv-2", null, "profile-1");
        });
        Assert.Contains("Resource not found", fixture.ViewModel.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        chatService.Verify(
            service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters => string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Contains(
            fixture.ViewModel.MessageHistory,
            message => string.Equals(message.TextContent, "cached local transcript", StringComparison.Ordinal));

        var workspaceBinding = fixture.Workspace.GetRemoteBinding("conv-2");
        Assert.NotNull(workspaceBinding);
        Assert.Null(workspaceBinding!.RemoteSessionId);
        Assert.Equal("profile-1", workspaceBinding.BoundProfileId);

        var state = await fixture.GetStateAsync();
        Assert.Equal(new ConversationBindingSlice("conv-2", null, "profile-1"), state.ResolveBinding("conv-2"));
    }

    [Fact]
    public async Task SwitchConversationAsync_RemoteBoundConversation_WhenBindingProjectionTimeout_UsesLocalFallbackToClearBinding()
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
            .ThrowsAsync(new AcpException(JsonRpcErrorCode.ResourceNotFound, "Resource not found: remote-2"));

        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.IsAny<ServerConfiguration>(),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<AcpConnectionContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected connect"));

        var bindingCommands = new Mock<IConversationBindingCommands>();
        bindingCommands
            .Setup(x => x.UpdateBindingAsync("conv-2", null, "profile-1"))
            .Returns(new ValueTask<BindingUpdateResult>(BindingUpdateResult.Error("BindingProjectionTimeout")));

        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            acpConnectionCommands: commands.Object,
            bindingCommands: bindingCommands.Object);
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

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");

        var switched = await fixture.ViewModel.SwitchConversationAsync("conv-2");

        Assert.True(switched);
        bindingCommands.Verify(x => x.UpdateBindingAsync("conv-2", null, "profile-1"), Times.Once);

        var workspaceBinding = fixture.Workspace.GetRemoteBinding("conv-2");
        Assert.NotNull(workspaceBinding);
        Assert.Null(workspaceBinding!.RemoteSessionId);
        Assert.Equal("profile-1", workspaceBinding.BoundProfileId);

        var state = await fixture.GetStateAsync();
        Assert.Equal(new ConversationBindingSlice("conv-2", null, "profile-1"), state.ResolveBinding("conv-2"));
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
        await DispatchConnectedAsync(fixture, "profile-1");
        syncContext.RunAll();

        var activationTask = fixture.ViewModel.SwitchConversationAsync("conv-2");

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
    [Trait("Suite", "Smoke")]
    public async Task ActivateConversationAsync_SwitchingBackToPreviouslyHydratedRemoteConversation_OnSameConnectionInstance_SkipsRemoteSessionReload()
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
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "local-1",
                    Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "local one"
                }
            ],
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
                    Id = "local-2",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "local two"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var loadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters => string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((parameters, _) =>
            {
                loadCounts[parameters.SessionId] = loadCounts.TryGetValue(parameters.SessionId, out var count) ? count + 1 : 1;
                return Task.FromResult(new SessionLoadResponse(
                    modes: new SessionModesState
                    {
                        CurrentModeId = "agent",
                        AvailableModes =
                        [
                            new SalmonEgg.Domain.Models.Protocol.SessionMode { Id = "agent", Name = "Agent" },
                            new SalmonEgg.Domain.Models.Protocol.SessionMode { Id = "plan", Name = "Plan" }
                        ]
                    },
                    configOptions: CreateModeConfigOptions("agent")));
            });
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters => string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((parameters, _) =>
            {
                loadCounts[parameters.SessionId] = loadCounts.TryGetValue(parameters.SessionId, out var count) ? count + 1 : 1;
                return Task.FromResult(new SessionLoadResponse(
                    modes: new SessionModesState
                    {
                        CurrentModeId = "debug",
                        AvailableModes =
                        [
                            new SalmonEgg.Domain.Models.Protocol.SessionMode { Id = "debug", Name = "Debug" },
                            new SalmonEgg.Domain.Models.Protocol.SessionMode { Id = "review", Name = "Review" }
                        ]
                    },
                    configOptions: CreateModeConfigOptions("debug")));
            });
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty
                .Add("conv-1", new ConversationContentSlice(
                    ImmutableList.Create(
                        new ConversationMessageSnapshot
                        {
                            Id = "local-1",
                            Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                            IsOutgoing = true,
                            ContentType = "text",
                            TextContent = "local one"
                        }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null))
                .Add("conv-2", new ConversationContentSlice(
                    ImmutableList.Create(
                        new ConversationMessageSnapshot
                        {
                            Id = "local-2",
                            Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                            IsOutgoing = true,
                            ContentType = "text",
                            TextContent = "local two"
                        }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

        Assert.Equal("conv-1", (await fixture.GetStateAsync()).HydratedConversationId);

        var initialHydrated = await fixture.ViewModel.HydrateActiveConversationAsync();
        Assert.True(initialHydrated);
        var initialState = await fixture.GetStateAsync();
        var initialRuntime = initialState.ResolveRuntimeState("conv-1");
        Assert.NotNull(initialRuntime);
        Assert.Equal(ConversationRuntimePhase.Warm, initialRuntime!.Value.Phase);
        Assert.Equal("conn-1", initialRuntime.Value.ConnectionInstanceId);
        Assert.True(ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            initialRuntime,
            initialState.ResolveBinding("conv-1"),
            fixture.ViewModel.ConnectionInstanceId));
        Assert.Equal("agent", fixture.ViewModel.SelectedMode?.ModeId);
        Assert.Equal("agent", fixture.ViewModel.ConfigOptions[0].Value);
        Assert.Collection(
            fixture.ViewModel.MessageHistory,
            message => Assert.Equal("local one", message.TextContent));

        await fixture.ViewModel.SwitchConversationAsync("conv-2");
        Assert.Collection(
            fixture.ViewModel.MessageHistory,
            message => Assert.Equal("local two", message.TextContent));

        await fixture.ViewModel.SwitchConversationAsync("conv-1");
        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);

        var roundTripState = await fixture.GetStateAsync();
        var roundTripRuntime = roundTripState.ResolveRuntimeState("conv-1");
        Assert.NotNull(roundTripRuntime);
        Assert.Equal(ConversationRuntimePhase.Warm, roundTripRuntime!.Value.Phase);
        Assert.Equal("conn-1", roundTripRuntime.Value.ConnectionInstanceId);
        Assert.True(ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            roundTripRuntime,
            roundTripState.ResolveBinding("conv-1"),
            fixture.ViewModel.ConnectionInstanceId));
        Assert.Equal(1, loadCounts.GetValueOrDefault("remote-1"));
        Assert.Equal(1, loadCounts.GetValueOrDefault("remote-2"));
        Assert.Collection(
            fixture.ViewModel.MessageHistory,
            message => Assert.Equal("local one", message.TextContent));
    }

    [Fact]
    [Trait("Suite", "Smoke")]
    public async Task ActivateConversationAsync_SwitchingBackAcrossProfiles_WhenOriginalConnectionIsReused_SkipsRemoteSessionReload()
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

        var loadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var serviceA = CreateConnectedChatService();
        serviceA.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        serviceA.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters => string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((parameters, _) =>
            {
                loadCounts[parameters.SessionId] = loadCounts.TryGetValue(parameters.SessionId, out var count) ? count + 1 : 1;
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        var serviceB = CreateConnectedChatService();
        serviceB.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        serviceB.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters => string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((parameters, _) =>
            {
                loadCounts[parameters.SessionId] = loadCounts.TryGetValue(parameters.SessionId, out var count) ? count + 1 : 1;
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-1", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                await sink.SelectProfileAsync(profile, cancellationToken);
                await sink.ReplaceChatServiceAsync(serviceA.Object, ServiceReplaceIntent.PoolOnly, cancellationToken);
                await DispatchConnectedAsync(fixture!, profile.Id);
                await fixture!.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
                return new AcpTransportApplyResult(serviceA.Object, new InitializeResponse());
            });
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-2", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-2", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                await sink.SelectProfileAsync(profile, cancellationToken);
                await sink.ReplaceChatServiceAsync(serviceB.Object, ServiceReplaceIntent.PoolOnly, cancellationToken);
                await DispatchConnectedAsync(fixture!, profile.Id);
                await fixture!.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-2"));
                return new AcpTransportApplyResult(serviceB.Object, new InitializeResponse());
            });

        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-2", "Profile 2"));
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-1",
                Transcript:
                [
                    new ConversationMessageSnapshot
                    {
                        Id = "local-1",
                        Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "local one"
                    }
                ],
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
                        Id = "local-2",
                        Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "local two"
                    }
                ],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(serviceA.Object));
            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                    .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-2"))
            });
            await DispatchConnectedAsync(fixture, "profile-1");
            await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await WaitForConditionAsync(() =>
                Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

            var initialHydrated = await fixture.ViewModel.HydrateActiveConversationAsync();
            Assert.True(initialHydrated);

            await fixture.ViewModel.SwitchConversationAsync("conv-2");
            Assert.Equal("conn-2", fixture.ViewModel.ConnectionInstanceId);
            Assert.Equal(1, loadCounts.GetValueOrDefault("remote-2"));

            await fixture.ViewModel.SwitchConversationAsync("conv-1");

            Assert.Equal("conn-1", fixture.ViewModel.ConnectionInstanceId);
            Assert.Equal(1, loadCounts.GetValueOrDefault("remote-1"));
            Assert.Equal(1, loadCounts.GetValueOrDefault("remote-2"));
            commands.Verify(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-1", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()), Times.Once);
            commands.Verify(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-2", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-2", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task ActivateConversationAsync_WhenCrossProfileSelectionSupersedesInFlightRemoteActivation_DoesNotSkipSessionLoad()
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

        var firstLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstLoadCanceled = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        var serviceA = CreateConnectedChatService();
        serviceA.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        serviceA.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)
                    && string.Equals(parameters.Cwd, @"C:\repo\one", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (parameters, cancellationToken) =>
            {
                loadCounts[parameters.SessionId] = loadCounts.TryGetValue(parameters.SessionId, out var count) ? count + 1 : 1;
                firstLoadStarted.TrySetResult(null);

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("Expected the superseded remote load to be canceled.");
                }
                catch (OperationCanceledException)
                {
                    firstLoadCanceled.TrySetResult(null);
                    throw;
                }
            });

        var serviceB = CreateConnectedChatService();
        serviceB.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        serviceB.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)
                    && string.Equals(parameters.Cwd, @"C:\repo\two", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((parameters, _) =>
            {
                loadCounts[parameters.SessionId] = loadCounts.TryGetValue(parameters.SessionId, out var count) ? count + 1 : 1;
                secondLoadStarted.TrySetResult(null);
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-2", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-2", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                await sink.SelectProfileAsync(profile, cancellationToken);
                await sink.ReplaceChatServiceAsync(serviceB.Object, ServiceReplaceIntent.PoolOnly, cancellationToken);
                await DispatchConnectedAsync(fixture!, profile.Id);
                await fixture!.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-2"));
                return new AcpTransportApplyResult(serviceB.Object, new InitializeResponse());
            });

        fixture = CreateViewModel(syncContext, sessionManager: sessionManager, acpConnectionCommands: commands.Object);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-2", "Profile 2"));
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
                Transcript:
                [
                    new ConversationMessageSnapshot
                    {
                        Id = "local-2",
                        Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "cached remote two"
                    }
                ],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(serviceA.Object));
            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-local",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                    .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-2")),
                ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty
                    .Add("conv-2", new ConversationContentSlice(
                        ImmutableList.Create(
                            new ConversationMessageSnapshot
                            {
                                Id = "local-2",
                                Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                                IsOutgoing = true,
                                ContentType = "text",
                                TextContent = "cached remote two"
                            }),
                        ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                        false,
                        null)),
                RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                    .Add("conv-2", new ConversationRuntimeSlice(
                        ConversationId: "conv-2",
                        Phase: ConversationRuntimePhase.Warm,
                        ConnectionInstanceId: "conn-2",
                        RemoteSessionId: "remote-2",
                        ProfileId: "profile-2",
                        Reason: "SessionLoadCompleted",
                        UpdatedAtUtc: new DateTime(2026, 3, 2, 1, 0, 0, DateTimeKind.Utc)))
            });
            await DispatchConnectedAsync(fixture, "profile-1");
            await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await WaitForConditionAsync(() =>
                Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

            var firstActivation = fixture.ViewModel.SwitchConversationAsync("conv-1");
            await firstLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var secondActivation = fixture.ViewModel.SwitchConversationAsync("conv-2");
            Assert.True(await secondActivation.WaitAsync(TimeSpan.FromSeconds(5)));

            await secondLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await firstLoadCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var finalState = await fixture.GetStateAsync();
            var finalRuntime = finalState.ResolveRuntimeState("conv-2");
            Assert.NotNull(finalRuntime);
            Assert.Equal("conn-2", fixture.ViewModel.ConnectionInstanceId);
            Assert.Equal(1, loadCounts.GetValueOrDefault("remote-2"));
            Assert.Equal("SessionLoadCompleted", finalRuntime!.Value.Reason);
            Assert.NotEqual("WarmReuseAfterProfileReconnect", finalRuntime.Value.Reason);
            Assert.False(await firstActivation);
        }
    }

    [Fact]
    [Trait("Suite", "Smoke")]
    public async Task ActivateConversationAsync_WarmRuntimeWithoutProjectedContent_StillReloadsRemoteSessionAndKeepsOverlayVisible()
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
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "local-2",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "local two"
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
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)
                    && string.Equals(parameters.Cwd, @"C:\repo\one", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, _) =>
            {
                loadStarted.TrySetResult(null);
                await allowLoadCompletion.Task;
                return SessionLoadResponse.Completed;
            });
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters => string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-2",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty
                .Add("conv-1", new ConversationContentSlice(
                    ImmutableList<ConversationMessageSnapshot>.Empty,
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null))
                .Add("conv-2", new ConversationContentSlice(
                    ImmutableList.Create(
                        new ConversationMessageSnapshot
                        {
                            Id = "local-2",
                            Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                            IsOutgoing = true,
                            ContentType = "text",
                            TextContent = "local two"
                        }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null)),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                .Add("conv-1", new ConversationRuntimeSlice(
                    ConversationId: "conv-1",
                    Phase: ConversationRuntimePhase.Warm,
                    ConnectionInstanceId: "conn-1",
                    RemoteSessionId: "remote-1",
                    ProfileId: "profile-1",
                    Reason: "SeedWarm",
                    UpdatedAtUtc: new DateTime(2026, 3, 1, 1, 0, 0, DateTimeKind.Utc)))
                .Add("conv-2", new ConversationRuntimeSlice(
                    ConversationId: "conv-2",
                    Phase: ConversationRuntimePhase.Warm,
                    ConnectionInstanceId: "conn-1",
                    RemoteSessionId: "remote-2",
                    ProfileId: "profile-1",
                    Reason: "ActiveWarm",
                    UpdatedAtUtc: new DateTime(2026, 3, 2, 1, 0, 0, DateTimeKind.Utc)))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        syncContext.RunAll();

        var activationTask = fixture.ViewModel.SwitchConversationAsync("conv-1");
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(loadStarted.Task.IsCompleted);
        }, timeoutMilliseconds: 2000);

        Assert.False(activationTask.IsCompleted, "Foreground switch should keep awaiting remote hydration until session/load completes.");
        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);
        Assert.True(fixture.ViewModel.IsOverlayVisible);
        Assert.Empty(fixture.ViewModel.MessageHistory);

        allowLoadCompletion.TrySetResult(null);
        await syncContext.RunUntilCompletedAsync(activationTask);
        Assert.True(await activationTask);
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(!fixture.ViewModel.IsOverlayVisible);
        }, timeoutMilliseconds: 7000);

        chatService.Verify(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)
                    && string.Equals(parameters.Cwd, @"C:\repo\one", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        chatService.Verify(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters => string.Equals(parameters.SessionId, "remote-2", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ActivateConversationAsync_SwitchingBackToWarmRemoteConversation_DoesNotShowSessionSwitchOverlayWhileSelectionCompletes()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var activationStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowActivationCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockReturnActivation = false;
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (sessionId, _) =>
            {
                if (blockReturnActivation && string.Equals(sessionId, "conv-1", StringComparison.Ordinal))
                {
                    activationStarted.TrySetResult(null);
                    await allowActivationCompletion.Task;
                }

                return new ConversationActivationResult(true, sessionId, null);
            });
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(
                It.IsAny<string>(),
                It.IsAny<ConversationActivationHydrationMode>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ConversationActivationHydrationMode, CancellationToken>(async (sessionId, _, _) =>
            {
                if (blockReturnActivation && string.Equals(sessionId, "conv-1", StringComparison.Ordinal))
                {
                    activationStarted.TrySetResult(null);
                    await allowActivationCompletion.Task;
                }

                return new ConversationActivationResult(true, sessionId, null);
            });

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

        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            conversationActivationCoordinator: activationCoordinator.Object);
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

        var loadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((parameters, _) =>
            {
                loadCounts[parameters.SessionId] = loadCounts.TryGetValue(parameters.SessionId, out var count) ? count + 1 : 1;
                return Task.FromResult(SessionLoadResponse.Completed);
            });
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

        await fixture.ViewModel.HydrateActiveConversationAsync();
        await fixture.ViewModel.SwitchConversationAsync("conv-2");

        blockReturnActivation = true;
        var switchBackTask = fixture.ViewModel.SwitchConversationAsync("conv-1");
        await activationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(fixture.ViewModel.IsOverlayVisible);
        Assert.False(fixture.ViewModel.ShouldShowLoadingOverlayStatusPill);
        Assert.False(fixture.ViewModel.ShouldShowBlockingLoadingMask);

        allowActivationCompletion.TrySetResult(null);
        await switchBackTask;

        Assert.Equal(1, loadCounts.GetValueOrDefault("remote-1"));
        Assert.Equal(1, loadCounts.GetValueOrDefault("remote-2"));
    }

    [Fact]
    public async Task HydrateActiveConversationAsync_WhenForegroundServiceOwnerDrifts_ReconnectsBoundProfileBeforeSessionLoad()
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

        var authoritativeLoadCount = 0;
        var driftedLoadCount = 0;
        var authoritativeInnerService = CreateConnectedChatService();
        authoritativeInnerService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        authoritativeInnerService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)
                    && string.Equals(parameters.Cwd, @"C:\repo\one", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((_, _) =>
            {
                authoritativeLoadCount++;
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        var driftedInnerService = CreateConnectedChatService();
        driftedInnerService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        driftedInnerService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((_, _) =>
            {
                driftedLoadCount++;
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        var adapterDispatcher = new ImmediateUiDispatcher();
        var authoritativeAdapter = new AcpChatServiceAdapter(
            authoritativeInnerService.Object,
            new AcpEventAdapter(_ => { }, adapterDispatcher));
        var driftedAdapter = new AcpChatServiceAdapter(
            driftedInnerService.Object,
            new AcpEventAdapter(_ => { }, adapterDispatcher));
        var connectionRegistry = new InMemoryAcpConnectionSessionRegistry();
        connectionRegistry.Upsert(new AcpConnectionSession(
            "profile-1",
            authoritativeAdapter,
            new InitializeResponse(),
            new AcpConnectionReuseKey(TransportType.Stdio, "agent-one.exe", string.Empty, string.Empty),
            ConnectionInstanceId: "conn-1"));
        connectionRegistry.Upsert(new AcpConnectionSession(
            "profile-2",
            driftedAdapter,
            new InitializeResponse(),
            new AcpConnectionReuseKey(TransportType.Stdio, "agent-two.exe", string.Empty, string.Empty),
            ConnectionInstanceId: "conn-2"));

        ViewModelFixture? fixture = null;
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        commands.Setup(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-1", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(async (profile, _, sink, _, cancellationToken) =>
            {
                await sink.SelectProfileAsync(profile, cancellationToken);
                await sink.ReplaceChatServiceAsync(authoritativeAdapter, ServiceReplaceIntent.PoolOnly, cancellationToken);
                await DispatchConnectedAsync(fixture!, profile.Id);
                await fixture!.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
                return new AcpTransportApplyResult(authoritativeAdapter, new InitializeResponse());
            });

        fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            acpConnectionCommands: commands.Object,
            connectionSessionRegistry: connectionRegistry);
        await using (fixture)
        {
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
            fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-2", "Profile 2"));
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(driftedAdapter));
            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-1",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
            });
            await DispatchConnectedAsync(fixture, "profile-1");
            await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await WaitForConditionAsync(() =>
                Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

            var hydrated = await fixture.ViewModel.HydrateActiveConversationAsync();

            Assert.True(hydrated);
            Assert.Equal(1, authoritativeLoadCount);
            Assert.Equal(0, driftedLoadCount);
            Assert.Same(authoritativeAdapter, fixture.ViewModel.CurrentChatService);
            commands.Verify(x => x.ConnectToProfileAsync(
                It.Is<ServerConfiguration>(profile => string.Equals(profile.Id, "profile-1", StringComparison.Ordinal)),
                It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.Is<AcpConnectionContext>(context =>
                    string.Equals(context.ConversationId, "conv-1", StringComparison.Ordinal)
                    && context.PreserveConversation),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
    [Fact]
    public async Task ActivateConversationAsync_SwitchingBackToPreviouslyHydratedRemoteConversation_AfterConnectionInstanceChanges_ReloadsRemoteSession()
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

        var loadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((parameters, _) =>
            {
                loadCounts[parameters.SessionId] = loadCounts.TryGetValue(parameters.SessionId, out var count) ? count + 1 : 1;
                return Task.FromResult(SessionLoadResponse.Completed);
            });
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

        await fixture.ViewModel.HydrateActiveConversationAsync();
        await fixture.ViewModel.SwitchConversationAsync("conv-2");
        var roundTripState = await fixture.GetStateAsync();
        var roundTripRuntime = roundTripState.ResolveRuntimeState("conv-1");
        Assert.NotNull(roundTripRuntime);
        Assert.Equal(ConversationRuntimePhase.Warm, roundTripRuntime!.Value.Phase);
        Assert.Equal("conn-1", roundTripRuntime.Value.ConnectionInstanceId);
        Assert.Equal("conn-1", fixture.ViewModel.ConnectionInstanceId);
        Assert.True(ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            roundTripRuntime,
            roundTripState.ResolveBinding("conv-1"),
            fixture.ViewModel.ConnectionInstanceId));
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-2"));
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-2", StringComparison.Ordinal)));
        var reboundState = await fixture.GetStateAsync();
        var reboundRuntime = reboundState.ResolveRuntimeState("conv-1");
        Assert.NotNull(reboundRuntime);
        Assert.Equal(ConversationRuntimePhase.Warm, reboundRuntime!.Value.Phase);
        Assert.Equal("conn-1", reboundRuntime.Value.ConnectionInstanceId);
        Assert.False(ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            reboundRuntime,
            reboundState.ResolveBinding("conv-1"),
            fixture.ViewModel.ConnectionInstanceId));
        await fixture.ViewModel.SwitchConversationAsync("conv-1");

        Assert.Equal(2, loadCounts.GetValueOrDefault("remote-1"));
        Assert.Equal(1, loadCounts.GetValueOrDefault("remote-2"));
    }

    [Fact]
    public async Task WarmLoadedRemoteConversation_FirstPrompt_ReusesLoadedRemoteSessionWithoutRecovery()
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

        var chatService = new ContinuityTrackingChatService();
        var chatServiceFactory = new Mock<IAcpChatServiceFactory>(MockBehavior.Strict);
        chatServiceFactory
            .Setup(factory => factory.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null))
            .Returns(chatService);

        var commandProxy = new ForwardingAcpConnectionCommands();
        IAcpConnectionCoordinator? connectionCoordinator = null;

        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            acpConnectionCommands: commandProxy,
            acpConnectionCoordinatorFactory: store =>
            {
                connectionCoordinator = new AcpConnectionCoordinator(
                    store,
                    Mock.Of<ILogger<AcpConnectionCoordinator>>());
                return connectionCoordinator;
            });

        commandProxy.Inner = new AcpChatCoordinator(
            chatServiceFactory.Object,
            Mock.Of<ILogger<AcpChatCoordinator>>(),
            connectionCoordinator: connectionCoordinator);

        var profile = CreateConnectableStdioProfile("profile-1", "Profile 1");
        fixture.Profiles.Profiles.Add(profile);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "msg-1",
                    Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "warm transcript"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));

        await fixture.UpdateStateAsync(state => state with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });

        await AwaitWithSynchronizationContextAsync(
            syncContext,
            fixture.ViewModel.ConnectToAcpProfileCommand.ExecuteAsync(profile));
        var switched = await fixture.ViewModel.SwitchConversationAsync("conv-1");
        Assert.True(switched, fixture.ViewModel.ErrorMessage);
        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            return string.Equals(state.HydratedConversationId, "conv-1", StringComparison.Ordinal)
                && string.Equals(state.ResolveBinding("conv-1")?.RemoteSessionId, "remote-1", StringComparison.Ordinal)
                && fixture.ViewModel.IsSessionActive;
        });

        Assert.Equal(1, chatService.LoadSessionCallCount);
        Assert.Equal("remote-1", Assert.Single(chatService.LoadedSessionIds));
        Assert.True(fixture.ViewModel.IsSessionActive);
        Assert.Equal("conv-1", fixture.ViewModel.CurrentSessionId);
        Assert.Equal("remote-1", fixture.ViewModel.CurrentRemoteSessionId);

        fixture.ViewModel.CurrentPrompt = "hello after warm load";
        await AwaitWithSynchronizationContextAsync(
            syncContext,
            fixture.ViewModel.SendPromptCommand.ExecuteAsync(null));

        Assert.Equal(1, chatService.SendPromptCallCount);
        Assert.Equal("remote-1", Assert.Single(chatService.PromptSessionIds));
        Assert.Equal(0, chatService.CreateSessionCallCount);
        chatServiceFactory.Verify(
            factory => factory.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null),
            Times.Once);
    }

    [Fact]
    public async Task WarmHydratedRemoteConversation_FirstPromptAfterReplayHydration_ReusesLoadedRemoteSessionWithoutRecovery()
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

        await sessionManager.Object.CreateSessionAsync("conv-1", @"C:\repo\stale");

        var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var innerChatService = new ContinuityTrackingChatService
        {
            OnLoadSessionAsync = (_, _) =>
            {
                loadStarted.TrySetResult(null);
                return Task.FromResult(SessionLoadResponse.Completed);
            },
            OnListSessionsAsync = (_, _) => Task.FromResult(new SessionListResponse
            {
                Sessions =
                [
                    new AgentSessionInfo
                    {
                        SessionId = "remote-1",
                        Title = "Remote title after warm hydration",
                        Cwd = @"C:\repo\fresh",
                        UpdatedAt = "2026-04-23T10:00:00Z"
                    }
                ]
            })
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            syncContext);
        adapter = new AcpChatServiceAdapter(innerChatService, eventAdapter);

        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(adapter));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 4, 23, 9, 55, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 4, 23, 9, 55, 0, DateTimeKind.Utc)));

        await fixture.UpdateStateAsync(state => state with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });

        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        syncContext.RunAll();

        var activationTask = fixture.ViewModel.SwitchConversationAsync("conv-1");
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(loadStarted.Task.IsCompleted);
        }, timeoutMilliseconds: 4000);

        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new UserMessageUpdate(new TextContentBlock("warm replay prompt"))));
        innerChatService.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("warm replay answer"))));

        await syncContext.RunUntilCompletedAsync(activationTask);
        Assert.True(await activationTask);

        await WaitForConditionAsync(async () =>
        {
            syncContext.RunAll();
            var state = await fixture.GetStateAsync();
            var runtime = state.ResolveRuntimeState("conv-1");
            return runtime is { Phase: ConversationRuntimePhase.Warm }
                && fixture.ViewModel.MessageHistory.Any(message =>
                    string.Equals(message.TextContent, "warm replay answer", StringComparison.Ordinal))
                && string.Equals(fixture.ViewModel.CurrentRemoteSessionId, "remote-1", StringComparison.Ordinal);
        }, timeoutMilliseconds: 8000);

        var warmedState = await fixture.GetStateAsync();
        var warmedRuntime = warmedState.ResolveRuntimeState("conv-1");
        Assert.NotNull(warmedRuntime);
        Assert.Equal(ConversationRuntimePhase.Warm, warmedRuntime!.Value.Phase);
        Assert.Equal("conn-1", warmedRuntime.Value.ConnectionInstanceId);
        Assert.Equal("remote-1", warmedRuntime.Value.RemoteSessionId);
        Assert.Equal("profile-1", warmedRuntime.Value.ProfileId);
        Assert.Equal(1, innerChatService.LoadSessionCallCount);
        Assert.Equal("remote-1", Assert.Single(innerChatService.LoadedSessionIds));
        if (innerChatService.ListSessionsCallCount > 0)
        {
            Assert.Equal(@"C:\repo\stale", sessions["conv-1"].Cwd);
        }

        fixture.ViewModel.CurrentPrompt = "hello after replay hydration";
        syncContext.RunAll();
        await AwaitWithSynchronizationContextAsync(
            syncContext,
            fixture.ViewModel.SendPromptCommand.ExecuteAsync(null));
        syncContext.RunAll();

        Assert.Equal(1, innerChatService.SendPromptCallCount);
        Assert.Equal("remote-1", Assert.Single(innerChatService.PromptSessionIds));
        Assert.Equal(0, innerChatService.CreateSessionCallCount);
    }

    [Fact]
    public async Task ActivateConversationAsync_WhenConnectionInstanceChangesDuringReturnActivation_ReloadsRemoteSession()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var activationStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowActivationCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockReturnActivation = false;
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (sessionId, _) =>
            {
                if (blockReturnActivation && string.Equals(sessionId, "conv-1", StringComparison.Ordinal))
                {
                    activationStarted.TrySetResult(null);
                    await allowActivationCompletion.Task;
                }

                return new ConversationActivationResult(true, sessionId, null);
            });
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(
                It.IsAny<string>(),
                It.IsAny<ConversationActivationHydrationMode>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ConversationActivationHydrationMode, CancellationToken>(async (sessionId, _, _) =>
            {
                if (blockReturnActivation && string.Equals(sessionId, "conv-1", StringComparison.Ordinal))
                {
                    activationStarted.TrySetResult(null);
                    await allowActivationCompletion.Task;
                }

                return new ConversationActivationResult(true, sessionId, null);
            });

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

        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            conversationActivationCoordinator: activationCoordinator.Object);
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

        var loadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((parameters, _) =>
            {
                loadCounts[parameters.SessionId] = loadCounts.TryGetValue(parameters.SessionId, out var count) ? count + 1 : 1;
                return Task.FromResult(SessionLoadResponse.Completed);
            });
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

        await fixture.ViewModel.HydrateActiveConversationAsync();
        await fixture.ViewModel.SwitchConversationAsync("conv-2");

        blockReturnActivation = true;
        var switchBackTask = fixture.ViewModel.SwitchConversationAsync("conv-1");
        await activationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-2"));
        await WaitForConditionAsync(() =>
            Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-2", StringComparison.Ordinal)));

        var inFlightState = await fixture.GetStateAsync();
        var inFlightRuntime = inFlightState.ResolveRuntimeState("conv-1");
        Assert.NotNull(inFlightRuntime);
        Assert.False(ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            inFlightRuntime,
            inFlightState.ResolveBinding("conv-1"),
            fixture.ViewModel.ConnectionInstanceId));

        allowActivationCompletion.TrySetResult(null);
        await switchBackTask;

        Assert.Equal(2, loadCounts.GetValueOrDefault("remote-1"));
        Assert.Equal(1, loadCounts.GetValueOrDefault("remote-2"));
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

        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));
        syncContext.RunAll();

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

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            var sessionSlice = state.ResolveSessionStateSlice("conv-1");
            syncContext.RunAll();
            return sessionSlice.HasValue
                && sessionSlice.Value.AvailableModes.Count == 2
                && string.Equals(sessionSlice.Value.SelectedModeId, "agent", StringComparison.Ordinal);
        });

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                viewModel.AvailableModes.Count == 2
                && string.Equals(viewModel.SelectedMode?.ModeId, "agent", StringComparison.Ordinal));
        });

        var targetMode = Assert.Single(viewModel.AvailableModes.Where(mode => string.Equals(mode.ModeId, "plan", StringComparison.Ordinal)));
        viewModel.SelectedMode = targetMode;

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            var sessionSlice = state.ResolveSessionStateSlice("conv-1");
            syncContext.RunAll();
            return capturedParams is not null
                && sessionSlice.HasValue
                && string.Equals(sessionSlice.Value.SelectedModeId, "plan", StringComparison.Ordinal)
                && sessionSlice.Value.ConfigOptions.Count == 1
                && string.Equals(sessionSlice.Value.ConfigOptions[0].SelectedValue, "plan", StringComparison.Ordinal);
        });

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                string.Equals(viewModel.SelectedMode?.ModeId, "plan", StringComparison.Ordinal)
                && viewModel.ConfigOptions.Count == 1
                && string.Equals(viewModel.ConfigOptions[0].Value?.ToString(), "plan", StringComparison.Ordinal));
        });

        Assert.NotNull(capturedParams);
        Assert.Equal("remote-1", capturedParams!.SessionId);
        Assert.Equal("mode", capturedParams.ConfigId);
        Assert.Equal("plan", capturedParams.Value);
        Assert.Equal("plan", viewModel.SelectedMode?.ModeId);
        Assert.Equal("plan", viewModel.ConfigOptions[0].Value);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_CurrentModeUpdate_WhenConfigOptionsExist_DoesNotOverrideSelectedMode()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();

        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));
        syncContext.RunAll();

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

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            var sessionSlice = state.ResolveSessionStateSlice("conv-1");
            syncContext.RunAll();
            return sessionSlice.HasValue
                && string.Equals(sessionSlice.Value.SelectedModeId, "agent", StringComparison.Ordinal)
                && sessionSlice.Value.ConfigOptions.Count == 1
                && string.Equals(sessionSlice.Value.ConfigOptions[0].SelectedValue, "agent", StringComparison.Ordinal);
        });

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new CurrentModeUpdate("plan")));

        await Task.Delay(50);
        syncContext.RunAll();

        var finalState = await fixture.GetStateAsync();
        var finalSessionSlice = finalState.ResolveSessionStateSlice("conv-1");

        Assert.NotNull(finalSessionSlice);
        Assert.Equal("agent", finalSessionSlice!.Value.SelectedModeId);
        Assert.Single(finalSessionSlice.Value.ConfigOptions);
        Assert.Equal("agent", finalSessionSlice.Value.ConfigOptions[0].SelectedValue);
        Assert.Equal("agent", viewModel.SelectedMode?.ModeId);
        Assert.Single(viewModel.ConfigOptions);
        Assert.Equal("agent", viewModel.ConfigOptions[0].Value);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_CurrentModeUpdate_WhenNoConfigOptions_StillProjectsLegacyMode()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();

        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));
        syncContext.RunAll();

        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty
                .Add(
                    "conv-1",
                    new ConversationSessionStateSlice(
                        ImmutableList.Create(
                            new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" },
                            new ConversationModeOptionSnapshot { ModeId = "plan", ModeName = "Plan" }),
                        "agent",
                        ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                        false,
                        ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                        null,
                        null))
        };
        await fixture.UpdateStateAsync(_ => initialState);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                string.Equals(viewModel.SelectedMode?.ModeId, "agent", StringComparison.Ordinal)
                && viewModel.ConfigOptions.Count == 0);
        });

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new CurrentModeUpdate("plan")));

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            var sessionSlice = state.ResolveSessionStateSlice("conv-1");
            syncContext.RunAll();
            return sessionSlice.HasValue
                && string.Equals(sessionSlice.Value.SelectedModeId, "plan", StringComparison.Ordinal);
        });

        var finalState = await fixture.GetStateAsync();
        var finalSessionSlice = finalState.ResolveSessionStateSlice("conv-1");

        Assert.NotNull(finalSessionSlice);
        Assert.Equal("plan", finalSessionSlice!.Value.SelectedModeId);
        Assert.Empty(finalSessionSlice.Value.ConfigOptions);
        Assert.Equal("plan", viewModel.SelectedMode?.ModeId);
        Assert.Empty(viewModel.ConfigOptions);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_CurrentModeUpdate_WhenConfigAuthorityWasEstablishedByEmptyConfigOptions_DoesNotProjectLegacyMode()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(chatService.Object);

        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        };
        await fixture.UpdateStateAsync(_ => initialState);

        var applySessionNewResponseAsync = typeof(ChatViewModel).GetMethod(
            "ApplySessionNewResponseAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(applySessionNewResponseAsync);

        var applyTask = (Task?)applySessionNewResponseAsync!.Invoke(
            viewModel,
            new object[]
            {
                "conv-1",
                new SessionNewResponse(
                    "remote-1",
                    modes: new SessionModesState
                    {
                        CurrentModeId = "agent",
                        AvailableModes = new List<SalmonEgg.Domain.Models.Protocol.SessionMode>
                        {
                            new() { Id = "agent", Name = "Agent" },
                            new() { Id = "plan", Name = "Plan" }
                        }
                    },
                    configOptions: new List<ConfigOption>())
            });
        Assert.NotNull(applyTask);
        await applyTask!;

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            var sessionSlice = state.ResolveSessionStateSlice("conv-1");
            return sessionSlice.HasValue
                && sessionSlice.Value.ConfigOptions.Count == 0
                && sessionSlice.Value.AvailableModes.Count == 0
                && sessionSlice.Value.SelectedModeId is null;
        });

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new CurrentModeUpdate("plan")));

        await Task.Delay(50);

        var finalState = await fixture.GetStateAsync();
        var finalSessionSlice = finalState.ResolveSessionStateSlice("conv-1");

        Assert.NotNull(finalSessionSlice);
        Assert.Empty(finalSessionSlice!.Value.ConfigOptions);
        Assert.Empty(finalSessionSlice.Value.AvailableModes);
        Assert.Null(finalSessionSlice.Value.SelectedModeId);
        Assert.Empty(viewModel.ConfigOptions);
        Assert.Empty(viewModel.AvailableModes);
        Assert.Null(viewModel.SelectedMode);
    }

    [Fact]
    public async Task ProcessSessionUpdateAsync_CurrentModeUpdate_WhenConversationRebindsToLegacyOnlySession_ProjectsLegacyMode()
    {
        var syncContext = new ImmediateSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var chatService = CreateConnectedChatService();
        viewModel.ReplaceChatService(chatService.Object);

        var initialState = (await fixture.GetStateAsync()) with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        };
        await fixture.UpdateStateAsync(_ => initialState);

        var applySessionNewResponseAsync = typeof(ChatViewModel).GetMethod(
            "ApplySessionNewResponseAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(applySessionNewResponseAsync);

        var establishConfigAuthorityTask = (Task?)applySessionNewResponseAsync!.Invoke(
            viewModel,
            new object[]
            {
                "conv-1",
                new SessionNewResponse(
                    "remote-1",
                    modes: new SessionModesState
                    {
                        CurrentModeId = "agent",
                        AvailableModes = new List<SalmonEgg.Domain.Models.Protocol.SessionMode>
                        {
                            new() { Id = "agent", Name = "Agent" },
                            new() { Id = "plan", Name = "Plan" }
                        }
                    },
                    configOptions: new List<ConfigOption>())
            });
        Assert.NotNull(establishConfigAuthorityTask);
        await establishConfigAuthorityTask!;

        var rebindLegacyOnlyTask = (Task?)applySessionNewResponseAsync.Invoke(
            viewModel,
            new object[]
            {
                "conv-1",
                new SessionNewResponse(
                    "remote-2",
                    modes: new SessionModesState
                    {
                        CurrentModeId = "agent",
                        AvailableModes = new List<SalmonEgg.Domain.Models.Protocol.SessionMode>
                        {
                            new() { Id = "agent", Name = "Agent" },
                            new() { Id = "plan", Name = "Plan" }
                        }
                    },
                    configOptions: null)
            });
        Assert.NotNull(rebindLegacyOnlyTask);
        await rebindLegacyOnlyTask!;

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            var sessionSlice = state.ResolveSessionStateSlice("conv-1");
            return sessionSlice.HasValue
                && sessionSlice.Value.ConfigOptions.Count == 0
                && sessionSlice.Value.AvailableModes.Count == 2
                && string.Equals(sessionSlice.Value.SelectedModeId, "agent", StringComparison.Ordinal);
        });

        chatService.Raise(
            service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-1", new CurrentModeUpdate("plan")));

        await WaitForConditionAsync(async () =>
        {
            var state = await fixture.GetStateAsync();
            var sessionSlice = state.ResolveSessionStateSlice("conv-1");
            return sessionSlice.HasValue
                && string.Equals(sessionSlice.Value.SelectedModeId, "plan", StringComparison.Ordinal);
        });

        var finalState = await fixture.GetStateAsync();
        var finalSessionSlice = finalState.ResolveSessionStateSlice("conv-1");

        Assert.NotNull(finalSessionSlice);
        Assert.Equal("plan", finalSessionSlice!.Value.SelectedModeId);
        Assert.Empty(finalSessionSlice.Value.ConfigOptions);
        Assert.Equal("plan", viewModel.SelectedMode?.ModeId);
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
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "conv-1",
                new ConversationSessionStateSlice(
                    ImmutableList.Create(
                        new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" },
                        new ConversationModeOptionSnapshot { ModeId = "plan", ModeName = "Plan" }),
                    "agent",
                    ImmutableList.Create(
                        new ConversationConfigOptionSnapshot { Id = "mode", Name = "Mode", SelectedValue = "agent" }),
                    true,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    null,
                    null))
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
        var sessionSlice = state.ResolveSessionStateSlice("conv-1");
        Assert.NotNull(sessionSlice);
        Assert.Equal(2, sessionSlice!.Value.AvailableModes.Count);
        Assert.Equal("agent", sessionSlice.Value.SelectedModeId);
        Assert.Single(sessionSlice.Value.ConfigOptions);
        Assert.True(sessionSlice.Value.ShowConfigOptionsPanel);
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
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.Preferences.IsLoaded);
        });

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
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.Preferences.IsLoaded);
        });

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
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.Preferences.IsLoaded);
        });

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
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(fixture.Preferences.IsLoaded);
        });
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



