using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class ConversationActivationCoordinatorTests
{
    [Fact]
    public async Task ActivateSessionAsync_SkipsHydrate_WhenGenerationIsNonZero()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1", "hello")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var state = State.Value(new object(), () => ChatState.Empty with { Generation = 1 });
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore.Object);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore.Object,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = await state;
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.True(currentState.Transcript is null or { Count: 0 });
    }

    [Fact]
    public async Task ActivateSessionAsync_DoesNotRehydrateBindingFromWorkspace_WhenGenerationIsNonZero()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-1", "profile-a");

        var state = State.Value(new object(), () => ChatState.Empty with { Generation = 1 });
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore.Object);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore.Object,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = await state;
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.Null(currentState.Binding);
    }

    [Fact]
    public async Task ActivateSessionAsync_SkipsHydrate_WhenStoreAlreadyHasBindingData()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1", "hello")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var state = State.Value(new object(), () => ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-1", "profile-1"))
        });
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore.Object);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore.Object,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = await state;
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.True(currentState.Transcript is null or { Count: 0 });
    }

    [Fact]
    public async Task ActivateSessionAsync_HydratesSnapshot_WhenStoreIsEmptyAndGenerationIsZero()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1", "hello")
            ],
            Plan:
            [
                new ConversationPlanEntrySnapshot
                {
                    Content = "step-1",
                    Status = PlanEntryStatus.InProgress,
                    Priority = PlanEntryPriority.High
                }
            ],
            ShowPlanPanel: true,
            PlanTitle: "plan",
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-1", "profile-a");

        var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore.Object);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore.Object,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = await state;
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.NotNull(currentState.Transcript);
        Assert.Single(currentState.Transcript!);
        Assert.NotNull(currentState.PlanEntries);
        Assert.Single(currentState.PlanEntries!);
        Assert.Null(currentState.Binding);
    }

    [Fact]
    public async Task ActivateSessionAsync_CommitsVisibleShellSelection_OnlyAfterActivationSucceeds()
    {
        var activationGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var selectionStore = new ShellSelectionStateStore();
        var projectSelectionStore = new RecordingProjectSelectionStore();
        var coordinator = new NavigationCoordinator(
            selectionStore,
            new ControlledConversationActivationCoordinator(activationGate.Task),
            projectSelectionStore,
            new StubShellNavigationService(ShellNavigationResult.Success()));

        var activationTask = coordinator.ActivateSessionAsync("session-1", "project-1");

        Assert.False(activationTask.IsCompleted);
        Assert.IsType<NavigationSelectionState.Start>(selectionStore.CurrentSelection);
        Assert.Null(projectSelectionStore.LastRememberedProjectId);

        activationGate.SetResult(true);

        var activated = await activationTask;

        Assert.True(activated);
        var selection = Assert.IsType<NavigationSelectionState.Session>(selectionStore.CurrentSelection);
        Assert.Equal("session-1", selection.SessionId);
        Assert.Equal("project-1", projectSelectionStore.LastRememberedProjectId);
    }

    [Fact]
    public async Task ActivateSessionAsync_ProfileMismatch_NormalizesStoreOwnedBinding_UsingConnectionStoreProfile()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1", "hello")
            ],
            Plan:
            [
                new ConversationPlanEntrySnapshot
                {
                    Content = "step-1",
                    Status = PlanEntryStatus.InProgress,
                    Priority = PlanEntryPriority.Medium
                }
            ],
            ShowPlanPanel: true,
            PlanTitle: "plan",
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-1", "profile-a");

        var state = State.Value(new object(), () => ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-1",
                new ConversationBindingSlice("session-1", "remote-1", "profile-a"))
        });
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore("profile-b");
        var bindingCommands = new BindingCoordinator(workspace, chatStore.Object);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore.Object,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        Assert.Equal("session-1", result.ConversationId);
        var currentState = await state;
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.Null(currentState.SelectedConversationId);
        Assert.Equal(new ConversationBindingSlice("session-1", null, "profile-b"), currentState.Binding);

        var workspaceBinding = workspace.GetRemoteBinding("session-1");
        Assert.NotNull(workspaceBinding);
        Assert.Equal("profile-b", workspaceBinding!.BoundProfileId);
        Assert.Null(workspaceBinding.RemoteSessionId);
    }

    [Fact]
    public async Task ArchiveConversation_CurrentSession_ClearsVisibleSelection_ThroughCoordinatorOnly()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var state = State.Value(new object(), () => ChatState.Empty);
        await state.Update(_ => ChatReducer.Reduce(ChatState.Empty, new SelectConversationAction("session-1")), default);
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore.Object);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore.Object,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ArchiveConversationAsync("session-1", "session-1");

        Assert.True(result.Succeeded);
        Assert.True(result.ClearedActiveConversation);
        Assert.DoesNotContain("session-1", workspace.GetKnownConversationIds());
        var currentState = await state;
        Assert.Null(currentState.HydratedConversationId);
        Assert.Null(currentState.SelectedConversationId);
    }

    [Fact]
    public async Task DeleteConversation_CurrentSession_ClearsVisibleSelection_ThroughCoordinatorOnly()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var state = State.Value(new object(), () => ChatState.Empty);
        await state.Update(_ => ChatReducer.Reduce(ChatState.Empty, new SelectConversationAction("session-1")), default);
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore.Object);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore.Object,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.DeleteConversationAsync("session-1", "session-1");

        Assert.True(result.Succeeded);
        Assert.True(result.ClearedActiveConversation);
        Assert.DoesNotContain("session-1", workspace.GetKnownConversationIds());
        var currentState = await state;
        Assert.Null(currentState.HydratedConversationId);
        Assert.Null(currentState.SelectedConversationId);
    }

    private static Mock<IChatStore> CreateChatStore(IState<ChatState> state)
    {
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action => state.Update(s => ChatReducer.Reduce(s!, action), default));
        return chatStore;
    }

    private static IChatConnectionStore CreateConnectionStore(string? selectedProfileId = null)
    {
        var state = State.Value(new object(), () => ChatConnectionState.Empty with
        {
            SelectedProfileId = null
        });
        var store = new ChatConnectionStore(state);
        if (!string.IsNullOrWhiteSpace(selectedProfileId))
        {
            store.Dispatch(new SetSelectedProfileAction(selectedProfileId)).AsTask().GetAwaiter().GetResult();
        }

        return store;
    }

    private static ChatConversationWorkspace CreateWorkspace(
        IConversationStore store,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        SynchronizationContext syncContext)
    {
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(syncContext);
            return new ChatConversationWorkspace(
                sessionManager,
                store,
                new AppPreferencesConversationWorkspacePreferences(preferences),
                Mock.Of<ILogger<ChatConversationWorkspace>>(),
                syncContext);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static AppPreferencesViewModel CreatePreferences(SynchronizationContext syncContext)
    {
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(syncContext);
            var appSettingsService = new Mock<IAppSettingsService>();
            appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
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
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static ConversationMessageSnapshot CreateTextMessage(string id, string text)
        => new()
        {
            Id = id,
            Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            IsOutgoing = false,
            ContentType = "text",
            TextContent = text
        };

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private sealed class CapturingConversationStore : IConversationStore
    {
        public ConversationDocument LoadResult { get; set; } = new();

        public object? LastSavedDocument { get; private set; }

        public Task<ConversationDocument> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(LoadResult);

        public Task SaveAsync(ConversationDocument document, CancellationToken cancellationToken = default)
        {
            LastSavedDocument = document;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionManager : ISessionManager
    {
        private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

        public IEnumerable<Session> GetAllSessions() => _sessions.Values;

        public Session? GetSession(string sessionId)
            => _sessions.TryGetValue(sessionId, out var session) ? session : null;

        public Task<Session> CreateSessionAsync(string sessionId, string? cwd = null)
        {
            var session = new Session(sessionId, cwd)
            {
                DisplayName = sessionId
            };
            _sessions[sessionId] = session;
            return Task.FromResult(session);
        }

        public bool RemoveSession(string sessionId) => _sessions.Remove(sessionId);

        public bool UpdateSession(string sessionId, Action<Session> updateAction, bool updateActivity = true)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return false;
            }

            updateAction(session);
            if (updateActivity)
            {
                session.LastActivityAt = DateTime.UtcNow;
            }

            return true;
        }

        public Task<bool> CancelSessionAsync(string sessionId, string? reason = null)
            => Task.FromResult(_sessions.ContainsKey(sessionId));
    }

    private sealed class ControlledConversationActivationCoordinator : IConversationActivationCoordinator
    {
        private readonly Task<bool> _activationTask;

        public ControlledConversationActivationCoordinator(Task<bool> activationTask)
        {
            _activationTask = activationTask;
        }

        public async Task<ConversationActivationResult> ActivateSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var activated = await _activationTask.WaitAsync(cancellationToken);
            return activated
                ? new ConversationActivationResult(true, sessionId, null)
                : new ConversationActivationResult(false, sessionId, "ActivationFailed");
        }

        public Task<ConversationMutationResult> ArchiveConversationAsync(string conversationId, string? activeConversationId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ConversationMutationResult(true, string.Equals(conversationId, activeConversationId, StringComparison.Ordinal), null));

        public Task<ConversationMutationResult> DeleteConversationAsync(string conversationId, string? activeConversationId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ConversationMutationResult(true, string.Equals(conversationId, activeConversationId, StringComparison.Ordinal), null));
    }

    private sealed class RecordingProjectSelectionStore : INavigationProjectSelectionStore
    {
        public string? LastRememberedProjectId { get; private set; }

        public void RememberSelectedProject(string? projectId)
        {
            LastRememberedProjectId = projectId;
        }
    }

    private sealed class StubShellNavigationService : IShellNavigationService
    {
        private readonly ShellNavigationResult _chatResult;

        public StubShellNavigationService(ShellNavigationResult chatResult)
        {
            _chatResult = chatResult;
        }

        public ValueTask<ShellNavigationResult> NavigateToSettings(string key)
            => ValueTask.FromResult(ShellNavigationResult.Success());

        public ValueTask<ShellNavigationResult> NavigateToChat()
            => ValueTask.FromResult(_chatResult);

        public ValueTask<ShellNavigationResult> NavigateToStart()
            => ValueTask.FromResult(ShellNavigationResult.Success());
    }
}
