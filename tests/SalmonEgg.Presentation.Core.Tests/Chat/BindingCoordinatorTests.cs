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
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class BindingCoordinatorTests
{
    [Fact]
    public async Task UpdateBinding_UpdatesStoreSlice_AndPersistsWorkspaceBinding()
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
        workspace.UpdateRemoteBinding("session-1", "remote-old", "profile-old");

        var state = State.Value(this, () => ChatState.Empty);
        var chatStore = CreateChatStore(state);
        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.UpdateBindingAsync("session-1", "remote-new", "profile-new");

        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Equal(
            new ConversationBindingSlice("session-1", "remote-new", "profile-new"),
            currentState.ResolveBinding("session-1"));

        var workspaceBinding = workspace.GetRemoteBinding("session-1");
        Assert.NotNull(workspaceBinding);
        Assert.Equal("remote-new", workspaceBinding!.RemoteSessionId);
        Assert.Equal("profile-new", workspaceBinding.BoundProfileId);
    }

    [Fact]
    public async Task UpdateBinding_WhenRemoteSessionIdIsRebound_ClearsPreviousOwner()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        await sessionManager.CreateSessionAsync("session-2", @"C:\repo\two");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "remote-1",
                    ContentType = "text",
                    TextContent = "remote transcript"
                }
            ],
            Plan:
            [
                new ConversationPlanEntrySnapshot
                {
                    Content = "Plan",
                    Status = PlanEntryStatus.Pending,
                    Priority = PlanEntryPriority.Medium
                }
            ],
            ShowPlanPanel: true,
            PlanTitle: "Remote plan",
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            AvailableModes:
            [
                new ConversationModeOptionSnapshot { ModeId = "mode-1", ModeName = "Mode 1" }
            ],
            SelectedModeId: "mode-1",
            ConfigOptions:
            [
                new ConversationConfigOptionSnapshot { Id = "cfg-1", Name = "Config 1", SelectedValue = "value" }
            ],
            ShowConfigOptionsPanel: true,
            AvailableCommands:
            [
                new ConversationAvailableCommandSnapshot("cmd-1", "Command 1", "desc")
            ],
            SessionInfo: new ConversationSessionInfoSnapshot { Title = "Remote title", Cwd = @"C:\repo\one" },
            Usage: new ConversationUsageSnapshot { Used = 1, Size = 2 }));
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-2",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-shared", "profile-1");
        workspace.UpdateRemoteBinding("session-2", "remote-old", "profile-2");

        var state = State.Value(this, () => ChatState.Empty with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("session-1", new ConversationBindingSlice("session-1", "remote-shared", "profile-1"))
                .Add("session-2", new ConversationBindingSlice("session-2", "remote-old", "profile-2")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "session-1",
                new ConversationContentSlice(
                    ImmutableList.Create(new ConversationMessageSnapshot
                    {
                        Id = "remote-1",
                        ContentType = "text",
                        TextContent = "remote transcript"
                    }),
                    ImmutableList.Create(new ConversationPlanEntrySnapshot
                    {
                        Content = "Plan",
                        Status = PlanEntryStatus.Pending,
                        Priority = PlanEntryPriority.Medium
                    }),
                    true,
                    "Remote plan")),
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "session-1",
                new ConversationSessionStateSlice(
                    ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "mode-1", ModeName = "Mode 1" }),
                    "mode-1",
                    ImmutableList.Create(new ConversationConfigOptionSnapshot { Id = "cfg-1", Name = "Config 1", SelectedValue = "value" }),
                    true,
                    ImmutableList.Create(new ConversationAvailableCommandSnapshot("cmd-1", "Command 1", "desc")),
                    new ConversationSessionInfoSnapshot { Title = "Remote title", Cwd = @"C:\repo\one" },
                    new ConversationUsageSnapshot { Used = 1, Size = 2 })),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty.Add(
                "session-1",
                new ConversationRuntimeSlice(
                    "session-1",
                    ConversationRuntimePhase.Warm,
                    "conn-1",
                    "remote-shared",
                    "profile-1",
                    "SessionLoadCompleted",
                    DateTime.UtcNow)),
            Transcript = ImmutableList.Create(new ConversationMessageSnapshot
            {
                Id = "remote-1",
                ContentType = "text",
                TextContent = "remote transcript"
            }),
            PlanEntries = ImmutableList.Create(new ConversationPlanEntrySnapshot
            {
                Content = "Plan",
                Status = PlanEntryStatus.Pending,
                Priority = PlanEntryPriority.Medium
            }),
            AvailableModes = ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "mode-1", ModeName = "Mode 1" }),
            SelectedModeId = "mode-1",
            ConfigOptions = ImmutableList.Create(new ConversationConfigOptionSnapshot { Id = "cfg-1", Name = "Config 1", SelectedValue = "value" }),
            ShowConfigOptionsPanel = true,
            AvailableCommands = ImmutableList.Create(new ConversationAvailableCommandSnapshot("cmd-1", "Command 1", "desc")),
            SessionInfo = new ConversationSessionInfoSnapshot { Title = "Remote title", Cwd = @"C:\repo\one" },
            Usage = new ConversationUsageSnapshot { Used = 1, Size = 2 },
            ShowPlanPanel = true,
            PlanTitle = "Remote plan"
        });
        var chatStore = CreateChatStore(state);
        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.UpdateBindingAsync("session-2", "remote-shared", "profile-2");

        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Null(currentState.ResolveBinding("session-1"));
        Assert.Equal(
            new ConversationBindingSlice("session-2", "remote-shared", "profile-2"),
            currentState.ResolveBinding("session-2"));
        Assert.Empty(currentState.ResolveContentSlice("session-1")?.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty);
        Assert.Empty(currentState.ResolveContentSlice("session-1")?.PlanEntries ?? ImmutableList<ConversationPlanEntrySnapshot>.Empty);
        Assert.Empty(currentState.ResolveSessionStateSlice("session-1")?.AvailableModes ?? ImmutableList<ConversationModeOptionSnapshot>.Empty);
        Assert.Empty(currentState.ResolveSessionStateSlice("session-1")?.ConfigOptions ?? ImmutableList<ConversationConfigOptionSnapshot>.Empty);
        Assert.Empty(currentState.ResolveSessionStateSlice("session-1")?.AvailableCommands ?? ImmutableList<ConversationAvailableCommandSnapshot>.Empty);
        Assert.Null(currentState.ResolveRuntimeState("session-1"));
        Assert.Equal("Remote title", currentState.ResolveSessionStateSlice("session-1")?.SessionInfo?.Title);
        Assert.Empty(currentState.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty);
        Assert.Empty(currentState.PlanEntries ?? ImmutableList<ConversationPlanEntrySnapshot>.Empty);
        Assert.Empty(currentState.AvailableModes ?? ImmutableList<ConversationModeOptionSnapshot>.Empty);
        Assert.Null(currentState.SelectedModeId);
        Assert.Empty(currentState.ConfigOptions ?? ImmutableList<ConversationConfigOptionSnapshot>.Empty);
        Assert.Empty(currentState.AvailableCommands ?? ImmutableList<ConversationAvailableCommandSnapshot>.Empty);
        Assert.Null(currentState.Usage);
        Assert.False(currentState.ShowPlanPanel);
        Assert.Null(currentState.PlanTitle);

        var workspaceBinding1 = workspace.GetRemoteBinding("session-1");
        Assert.NotNull(workspaceBinding1);
        Assert.Null(workspaceBinding1!.RemoteSessionId);
        Assert.Null(workspaceBinding1.BoundProfileId);
        var workspaceSnapshot1 = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(workspaceSnapshot1);
        Assert.Empty(workspaceSnapshot1!.Transcript);
        Assert.Empty(workspaceSnapshot1.Plan);
        Assert.Empty(workspaceSnapshot1.AvailableModes ?? Array.Empty<ConversationModeOptionSnapshot>());
        Assert.Null(workspaceSnapshot1.SelectedModeId);
        Assert.Empty(workspaceSnapshot1.ConfigOptions ?? Array.Empty<ConversationConfigOptionSnapshot>());
        Assert.Empty(workspaceSnapshot1.AvailableCommands ?? Array.Empty<ConversationAvailableCommandSnapshot>());
        Assert.NotNull(workspaceSnapshot1.SessionInfo);
        Assert.Null(workspaceSnapshot1.Usage);
        Assert.False(workspaceSnapshot1.ShowPlanPanel);
        Assert.Null(workspaceSnapshot1.PlanTitle);

        var workspaceBinding2 = workspace.GetRemoteBinding("session-2");
        Assert.NotNull(workspaceBinding2);
        Assert.Equal("remote-shared", workspaceBinding2!.RemoteSessionId);
        Assert.Equal("profile-2", workspaceBinding2.BoundProfileId);
    }

    [Fact]
    public async Task ClearBindingAsync_WhenConversationWasRemoteBacked_ScrubsRemoteDerivedState()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(
            new ConversationWorkspaceSnapshot(
                ConversationId: "session-1",
                Transcript:
                [
                    new ConversationMessageSnapshot
                    {
                        Id = "remote-1",
                        ContentType = "text",
                        TextContent = "remote transcript"
                    }
                ],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                SessionInfo: new ConversationSessionInfoSnapshot { Title = "Remote title", Cwd = @"C:\repo\one" }),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        workspace.UpdateRemoteBinding("session-1", "remote-old", "profile-old");

        var state = State.Value(this, () => ChatState.Empty with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("session-1", new ConversationBindingSlice("session-1", "remote-old", "profile-old")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "session-1",
                new ConversationContentSlice(
                    ImmutableList.Create(new ConversationMessageSnapshot
                    {
                        Id = "remote-1",
                        ContentType = "text",
                        TextContent = "remote transcript"
                    }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null)),
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "session-1",
                new ConversationSessionStateSlice(
                    ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    new ConversationSessionInfoSnapshot { Title = "Remote title", Cwd = @"C:\repo\one" },
                    new ConversationUsageSnapshot { Used = 1, Size = 2 })),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty.Add(
                "session-1",
                new ConversationRuntimeSlice(
                    "session-1",
                    ConversationRuntimePhase.Warm,
                    "conn-1",
                    "remote-old",
                    "profile-old",
                    "SessionLoadCompleted",
                    DateTime.UtcNow)),
            Transcript = ImmutableList.Create(new ConversationMessageSnapshot
            {
                Id = "remote-1",
                ContentType = "text",
                TextContent = "remote transcript"
            }),
            SessionInfo = new ConversationSessionInfoSnapshot { Title = "Remote title", Cwd = @"C:\repo\one" },
            Usage = new ConversationUsageSnapshot { Used = 1, Size = 2 }
        });
        var chatStore = CreateChatStore(state);
        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.ClearBindingAsync("session-1");

        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Null(currentState.ResolveBinding("session-1"));
        Assert.Empty(currentState.ResolveContentSlice("session-1")?.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty);
        Assert.Null(currentState.ResolveRuntimeState("session-1"));
        Assert.Equal("Remote title", currentState.ResolveSessionStateSlice("session-1")?.SessionInfo?.Title);
        Assert.Empty(currentState.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty);
        Assert.Null(currentState.Usage);

        var workspaceBinding = workspace.GetRemoteBinding("session-1");
        Assert.NotNull(workspaceBinding);
        Assert.Null(workspaceBinding!.RemoteSessionId);
        Assert.Null(workspaceBinding.BoundProfileId);
        var workspaceSnapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(workspaceSnapshot);
        Assert.Empty(workspaceSnapshot!.Transcript);
        Assert.NotNull(workspaceSnapshot.SessionInfo);
        Assert.Null(workspaceSnapshot.Usage);
    }

    [Fact]
    public async Task UpdateBinding_DoesNotPersistWorkspaceBinding_WhenStoreDispatchFails()
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
        workspace.UpdateRemoteBinding("session-1", "remote-old", "profile-old");

        var state = State.Value(this, () => ChatState.Empty);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .ThrowsAsync(new InvalidOperationException("store dispatch failed"));
        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.UpdateBindingAsync("session-1", "remote-new", "profile-new");

        Assert.Equal(BindingUpdateStatus.Error, result.Status);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Null(currentState.ResolveBinding("session-1"));

        var workspaceBinding = workspace.GetRemoteBinding("session-1");
        Assert.NotNull(workspaceBinding);
        Assert.Equal("remote-old", workspaceBinding!.RemoteSessionId);
        Assert.Equal("profile-old", workspaceBinding.BoundProfileId);
    }

    [Fact]
    public async Task UpdateBinding_WaitsUntilBindingProjectionIsVisible()
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

        var state = State.Value(this, () => ChatState.Empty);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(50);
                    await state.Update(s => ChatReducer.Reduce(s!, action), default);
                });

                return ValueTask.CompletedTask;
            });

        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.UpdateBindingAsync("session-1", "remote-new", "profile-new");

        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Equal(
            new ConversationBindingSlice("session-1", "remote-new", "profile-new"),
            currentState.ResolveBinding("session-1"));
    }

    [Fact]
    public async Task UpdateBinding_WhenProjectionIsSlowStillSucceedsBeforeTimingOut()
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

        var state = State.Value(this, () => ChatState.Empty);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(750);
                    await state.Update(s => ChatReducer.Reduce(s!, action), default);
                });

                return ValueTask.CompletedTask;
            });

        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.UpdateBindingAsync("session-1", "remote-new", "profile-new");

        Assert.Equal(BindingUpdateStatus.Success, result.Status);
    }

    private static Mock<IChatStore> CreateChatStore(IState<ChatState> state)
    {
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action => state.Update(s => ChatReducer.Reduce(s!, action), default));
        return chatStore;
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
                new ImmediateUiDispatcher());
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
                prefsLogger.Object,
                new ImmediateUiDispatcher());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private sealed class CapturingConversationStore : IConversationStore
    {
        public ConversationDocument LoadResult { get; set; } = new();

        public Task<ConversationDocument> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(LoadResult);

        public Task SaveAsync(ConversationDocument document, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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
}
