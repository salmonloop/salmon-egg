using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class ChatLaunchWorkflowTests
{
    [Fact]
    public async Task StartSessionAndSendAsync_UsesNavigationActivationAsSingleSessionOwner()
    {
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string id, string? cwd) => new Session { SessionId = id, Cwd = cwd });

        var preferences = CreatePreferences(lastSelectedProjectId: "project-1");

        var chat = new FakeChatLaunchWorkflowChatFacade
        {
            IsConnected = true
        };
        var navigation = new RecordingNavigationCoordinator(chat)
        {
            ApplyActivatedSessionToChat = true
        };
        var logger = new Mock<ILogger<ChatLaunchWorkflow>>();

        var workflow = new ChatLaunchWorkflow(
            chat,
            sessionManager.Object,
            preferences,
            navigation,
            () => @"C:\repo\demo",
            logger.Object);

        await workflow.StartSessionAndSendAsync("hello");

        sessionManager.Verify(s => s.CreateSessionAsync(It.IsAny<string>(), @"C:\repo\demo"), Times.Once);
        Assert.Equal(1, navigation.ActivateSessionCount);
        Assert.Equal(0, chat.AutoConnectCallCount);
        Assert.Equal(1, chat.SendPromptCount);
        Assert.Equal(0, navigation.ActivateSettingsCount);
        Assert.Equal(navigation.LastActivatedSessionId, chat.CurrentSessionId);
    }

    [Fact]
    public async Task StartSessionAndSendAsync_WhenNavigationActivationFails_DoesNotContinue()
    {
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string id, string? cwd) => new Session { SessionId = id, Cwd = cwd });

        var preferences = CreatePreferences();
        var chat = new FakeChatLaunchWorkflowChatFacade
        {
            IsConnected = true
        };
        var navigation = new RecordingNavigationCoordinator(chat)
        {
            ActivationSucceeded = false
        };
        var logger = new Mock<ILogger<ChatLaunchWorkflow>>();

        var workflow = new ChatLaunchWorkflow(
            chat,
            sessionManager.Object,
            preferences,
            navigation,
            () => @"C:\repo\demo",
            logger.Object);

        await workflow.StartSessionAndSendAsync("hello");

        Assert.Equal(1, navigation.ActivateSessionCount);
        Assert.Equal(0, chat.AutoConnectCallCount);
        Assert.Equal(0, chat.SendPromptCount);
        Assert.Equal(0, navigation.ActivateSettingsCount);
    }

    [Fact]
    public async Task StartSessionAndSendAsync_WhenAutoConnectStillInProgress_DoesNotOpenSettingsOrSend()
    {
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string id, string? cwd) => new Session { SessionId = id, Cwd = cwd });

        var preferences = CreatePreferences();
        var chat = new FakeChatLaunchWorkflowChatFacade
        {
            IsConnected = false,
            IsConnecting = false,
            AutoConnectAction = facade =>
            {
                facade.IsConnecting = true;
                facade.IsConnected = false;
            }
        };
        var navigation = new RecordingNavigationCoordinator(chat)
        {
            ApplyActivatedSessionToChat = true
        };
        var logger = new Mock<ILogger<ChatLaunchWorkflow>>();

        var workflow = new ChatLaunchWorkflow(
            chat,
            sessionManager.Object,
            preferences,
            navigation,
            () => @"C:\repo\demo",
            logger.Object);

        await workflow.StartSessionAndSendAsync("hello");

        Assert.Equal(1, chat.AutoConnectCallCount);
        Assert.Equal(0, navigation.ActivateSettingsCount);
        Assert.Equal(0, chat.SendPromptCount);
        Assert.False(chat.ShowTransportConfigPanel);
    }

    [Fact]
    public async Task StartSessionAndSendAsync_WhenStillDisconnectedAfterAutoConnect_OpensSettingsAndTransportConfig()
    {
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string id, string? cwd) => new Session { SessionId = id, Cwd = cwd });

        var preferences = CreatePreferences();
        var chat = new FakeChatLaunchWorkflowChatFacade
        {
            IsConnected = false,
            AutoConnectAction = facade =>
            {
                facade.IsConnected = false;
                facade.IsConnecting = false;
                facade.IsInitializing = false;
            }
        };
        var navigation = new RecordingNavigationCoordinator(chat)
        {
            ApplyActivatedSessionToChat = true
        };
        var logger = new Mock<ILogger<ChatLaunchWorkflow>>();

        var workflow = new ChatLaunchWorkflow(
            chat,
            sessionManager.Object,
            preferences,
            navigation,
            () => @"C:\repo\demo",
            logger.Object);

        await workflow.StartSessionAndSendAsync("hello");

        Assert.Equal(1, chat.AutoConnectCallCount);
        Assert.Equal(1, navigation.ActivateSettingsCount);
        Assert.Equal("General", navigation.LastSettingsKey);
        Assert.True(chat.ShowTransportConfigPanel);
        Assert.Equal(0, chat.SendPromptCount);
    }

    [Fact]
    public async Task StartSessionAndSendAsync_RegistersConversationBeforePromptDispatch()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(lastSelectedProjectId: null);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        using var catalogFacade = new ConversationCatalogFacade(
            workspace,
            new NavigationProjectPreferencesAdapter(preferences),
            Mock.Of<IConversationActivationCoordinator>(),
            Mock.Of<IShellSelectionReadModel>(),
            Mock.Of<INavigationCoordinator>(),
            Mock.Of<ILogger<ConversationCatalogFacade>>());

        var chat = new FakeChatLaunchWorkflowChatFacade
        {
            IsConnected = true
        };
        var navigation = new RecordingNavigationCoordinator(chat)
        {
            ApplyActivatedSessionToChat = true,
            OnActivateSession = sessionId =>
            {
                Assert.Contains(sessionId, workspace.GetKnownConversationIds());
                Assert.Equal(1, workspace.ConversationListVersion);
            }
        };
        var logger = new Mock<ILogger<ChatLaunchWorkflow>>();

        var workflow = new ChatLaunchWorkflow(
            chat,
            sessionManager,
            preferences,
            navigation,
            () => @"C:\repo\demo",
            logger.Object,
            catalogFacade);

        await workflow.StartSessionAndSendAsync("hello");

        Assert.Single(workspace.GetKnownConversationIds());
        Assert.Equal(1, workspace.ConversationListVersion);
        Assert.NotNull(navigation.LastActivatedSessionId);
        Assert.Contains(navigation.LastActivatedSessionId, workspace.GetKnownConversationIds());
    }

    [Fact]
    public async Task StartSessionAndSendAsync_PersistsRegisteredConversationAfterCatalogRegistration()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(lastSelectedProjectId: null);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        using var catalogFacade = new ConversationCatalogFacade(
            workspace,
            new NavigationProjectPreferencesAdapter(preferences),
            Mock.Of<IConversationActivationCoordinator>(),
            Mock.Of<IShellSelectionReadModel>(),
            Mock.Of<INavigationCoordinator>(),
            Mock.Of<ILogger<ConversationCatalogFacade>>());

        var chat = new FakeChatLaunchWorkflowChatFacade
        {
            IsConnected = true
        };
        var navigation = new RecordingNavigationCoordinator(chat)
        {
            ApplyActivatedSessionToChat = true
        };

        var workflow = new ChatLaunchWorkflow(
            chat,
            sessionManager,
            preferences,
            navigation,
            () => @"C:\repo\demo",
            catalogFacade: catalogFacade);

        await workflow.StartSessionAndSendAsync("hello");

        var saved = await store.WaitForSaveAsync();

        Assert.NotNull(navigation.LastActivatedSessionId);
        Assert.Contains(saved.Conversations, conversation => conversation.ConversationId == navigation.LastActivatedSessionId);
    }

    [Fact]
    public async Task StartSessionAndSendAsync_ForwardsCancellationTokenToAutoConnect()
    {
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string id, string? cwd) => new Session { SessionId = id, Cwd = cwd });

        var preferences = CreatePreferences();
        var chat = new FakeChatLaunchWorkflowChatFacade
        {
            IsConnected = false,
            AutoConnectAction = facade => facade.IsConnected = true
        };
        var navigation = new RecordingNavigationCoordinator(chat)
        {
            ApplyActivatedSessionToChat = true
        };

        var workflow = new ChatLaunchWorkflow(
            chat,
            sessionManager.Object,
            preferences,
            navigation,
            () => @"C:\repo\demo");

        using var cts = new CancellationTokenSource();
        await workflow.StartSessionAndSendAsync("hello", cts.Token);

        Assert.Equal(cts.Token, chat.LastAutoConnectToken);
        Assert.Equal(1, chat.AutoConnectCallCount);
    }

    private static AppPreferencesViewModel CreatePreferences(string? lastSelectedProjectId = null)
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings
        {
            LastSelectedProjectId = lastSelectedProjectId
        });
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        return new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object);
    }

    private sealed class FakeChatLaunchWorkflowChatFacade : IChatLaunchWorkflowChatFacade
    {
        private bool _isConnected;
        private string? _currentSessionId;

        public bool IsConnected
        {
            get => _isConnected;
            set => _isConnected = value;
        }

        public bool IsConnecting { get; set; }

        public bool IsInitializing { get; set; }

        public bool ShowTransportConfigPanel { get; set; }

        public string? CurrentSessionId
        {
            get => _currentSessionId;
            private set => _currentSessionId = value;
        }

        public int AutoConnectCallCount { get; private set; }

        public int SendPromptCount { get; private set; }

        public string? PreparedPrompt { get; private set; }

        public Action<FakeChatLaunchWorkflowChatFacade>? AutoConnectAction { get; set; }

        public CancellationToken LastAutoConnectToken { get; private set; }

        public Task<ChatLaunchConnectionOutcome> EnsureConnectedForLaunchAsync(CancellationToken cancellationToken = default)
        {
            if (_isConnected)
            {
                return Task.FromResult(ChatLaunchConnectionOutcome.Connected);
            }

            AutoConnectCallCount++;
            LastAutoConnectToken = cancellationToken;
            AutoConnectAction?.Invoke(this);

            if (_isConnected)
            {
                return Task.FromResult(ChatLaunchConnectionOutcome.Connected);
            }

            return Task.FromResult(IsConnecting || IsInitializing
                ? ChatLaunchConnectionOutcome.InProgress
                : ChatLaunchConnectionOutcome.RequiresConfiguration);
        }

        public bool TrySendPromptForLaunch()
        {
            SendPromptCount++;
            return true;
        }

        public void PrepareDraftForLaunch(string promptText)
        {
            PreparedPrompt = promptText;
        }

        public void ApplyActivatedSession(string sessionId)
        {
            CurrentSessionId = sessionId;
        }
    }

    private sealed class RecordingNavigationCoordinator : INavigationCoordinator
    {
        private readonly FakeChatLaunchWorkflowChatFacade _chat;

        public RecordingNavigationCoordinator(FakeChatLaunchWorkflowChatFacade chat)
        {
            _chat = chat;
        }

        public int ActivateSessionCount { get; private set; }

        public int ActivateSettingsCount { get; private set; }

        public bool ApplyActivatedSessionToChat { get; set; }

        public bool ActivationSucceeded { get; set; } = true;

        public string? LastActivatedSessionId { get; private set; }

        public string? LastActivatedProjectId { get; private set; }

        public string? LastSettingsKey { get; private set; }

        public Action<string>? OnActivateSession { get; set; }

        public Task ActivateStartAsync() => Task.CompletedTask;

        public Task ActivateDiscoverSessionsAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey)
        {
            ActivateSettingsCount++;
            LastSettingsKey = settingsKey;
            return Task.CompletedTask;
        }

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId)
        {
            ActivateSessionCount++;
            LastActivatedSessionId = sessionId;
            LastActivatedProjectId = projectId;

            OnActivateSession?.Invoke(sessionId);

            if (!ActivationSucceeded)
            {
                return Task.FromResult(false);
            }

            if (ApplyActivatedSessionToChat)
            {
                // Navigation remains the single switch owner for the Start launch path.
                _chat.ApplyActivatedSession(sessionId);
            }

            return Task.FromResult(true);
        }

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private sealed class CapturingConversationStore : IConversationStore
    {
        private readonly TaskCompletionSource<ConversationDocument> _saveTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConversationDocument LoadResult { get; set; } = new();

        public ConversationDocument? LastSavedDocument { get; private set; }

        public Task<ConversationDocument> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(LoadResult);

        public Task SaveAsync(ConversationDocument document, CancellationToken cancellationToken = default)
        {
            LastSavedDocument = document;
            _saveTcs.TrySetResult(document);
            return Task.CompletedTask;
        }

        public async Task<ConversationDocument> WaitForSaveAsync(int timeoutMilliseconds = 2000)
        {
            var completedTask = await Task.WhenAny(_saveTcs.Task, Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
            if (completedTask != _saveTcs.Task)
            {
                throw new TimeoutException("Timed out waiting for conversation workspace persistence.");
            }

            return await _saveTcs.Task.ConfigureAwait(false);
        }
    }

    private sealed class FakeSessionManager : ISessionManager
    {
        private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

        public Task<Session> CreateSessionAsync(string sessionId, string? cwd = null)
        {
            var session = new Session(sessionId, cwd);
            _sessions[sessionId] = session;
            return Task.FromResult(session);
        }

        public Session? GetSession(string sessionId)
            => _sessions.TryGetValue(sessionId, out var session) ? session : null;

        public bool UpdateSession(string sessionId, Action<Session> updateAction, bool updateActivity = true)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return false;
            }

            updateAction(session);
            if (updateActivity)
            {
                session.UpdateActivity();
            }

            return true;
        }

        public Task<bool> CancelSessionAsync(string sessionId, string? reason = null)
            => Task.FromResult(_sessions.ContainsKey(sessionId));

        public IEnumerable<Session> GetAllSessions()
            => _sessions.Values;

        public bool RemoveSession(string sessionId)
            => _sessions.Remove(sessionId);
    }

    private static ChatConversationWorkspace CreateWorkspace(
        IConversationStore store,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        SynchronizationContext synchronizationContext)
    {
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            return new ChatConversationWorkspace(
                sessionManager,
                store,
                new AppPreferencesConversationWorkspacePreferences(preferences),
                Mock.Of<ILogger<ChatConversationWorkspace>>(),
                synchronizationContext);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }
}
