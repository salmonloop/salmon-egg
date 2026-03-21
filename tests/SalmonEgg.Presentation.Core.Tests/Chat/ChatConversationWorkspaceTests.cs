using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class ChatConversationWorkspaceTests
{
    [Fact]
    public async Task RestoreAsync_RestoresLastActiveConversationAndTranscript()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore
        {
            LoadResult = new ConversationDocument
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
                        Cwd = @"C:\repo\one",
                        RemoteSessionId = "remote-1",
                        BoundProfileId = "profile-a",
                        Messages =
                        {
                            CreateTextMessage("m-1", "hello")
                        }
                    },
                    new ConversationRecord
                    {
                        ConversationId = "session-2",
                        DisplayName = "Session Two",
                        CreatedAt = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedAt = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc),
                        Cwd = @"C:\repo\two",
                        Messages =
                        {
                            CreateTextMessage("m-2", "world")
                        }
                    }
                }
            }
        };

        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        await workspace.RestoreAsync();

        Assert.False(workspace.IsConversationListLoading);
        Assert.Equal(1, workspace.ConversationListVersion);
        Assert.Equal("session-1", workspace.LastActiveConversationId);
        Assert.Equal(new[] { "session-2", "session-1" }, workspace.GetKnownConversationIds());

        var snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Transcript);
        Assert.Equal("hello", snapshot.Transcript[0].TextContent);
        var remoteBinding = workspace.GetRemoteBinding("session-1");
        Assert.NotNull(remoteBinding);
        Assert.Equal("remote-1", remoteBinding!.RemoteSessionId);
        Assert.Equal("profile-a", remoteBinding.BoundProfileId);

        var session = sessionManager.GetSession("session-1");
        Assert.NotNull(session);
        Assert.Equal("Session One", session!.DisplayName);
        Assert.Equal(@"C:\repo\one", session.Cwd);
    }

    [Fact]
    public async Task RestoreAsync_DoesNotOwnSemanticCurrentSessionSelection()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore
        {
            LoadResult = new ConversationDocument
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
                        Cwd = @"C:\repo\one",
                        Messages =
                        {
                            CreateTextMessage("m-1", "hello")
                        }
                    }
                }
            }
        };

        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        await workspace.RestoreAsync();

        Assert.Null(workspace.CurrentConversationId);
    }

    [Fact]
    public async Task TrySwitchToSessionAsync_ProfileMismatch_KeepsRemoteBindingAndLocalTranscript()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");

        var preferences = CreatePreferences(syncContext);
        preferences.LastSelectedServerId = "profile-b";

        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
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

        var switched = await workspace.TrySwitchToSessionAsync("session-1");

        Assert.True(switched);
        Assert.Equal("session-1", workspace.LastActiveConversationId);

        var snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Transcript);
        Assert.Equal("hello", snapshot.Transcript[0].TextContent);
        Assert.Single(snapshot.Plan);
        Assert.Equal("step-1", snapshot.Plan[0].Content);
        Assert.True(snapshot.ShowPlanPanel);
        Assert.Equal("plan", snapshot.PlanTitle);

        var remoteBinding = workspace.GetRemoteBinding("session-1");
        Assert.NotNull(remoteBinding);
        Assert.Equal("remote-1", remoteBinding!.RemoteSessionId);
        Assert.Equal("profile-a", remoteBinding.BoundProfileId);
    }

    [Fact]
    public async Task TrySwitchToSessionAsync_DoesNotOwnSemanticCurrentSessionSelection()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
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

        var switched = await workspace.TrySwitchToSessionAsync("session-1");

        Assert.True(switched);
        Assert.Null(workspace.CurrentConversationId);
    }

    [Fact]
    public async Task SaveAsync_PersistsDisplayNameCwdAndTranscriptInMostRecentOrder()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var older = await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        older.DisplayName = "Older";
        var newer = await sessionManager.CreateSessionAsync("session-2", @"C:\repo\two");
        newer.DisplayName = "Newer";

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1", "older")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-2",
            Transcript:
            [
                CreateTextMessage("m-2", "newer")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-older", "profile-older");
        workspace.UpdateRemoteBinding("session-2", "remote-newer", "profile-newer");

        await workspace.TrySwitchToSessionAsync("session-2");
        await workspace.SaveAsync();

        var saved = Assert.IsType<ConversationDocument>(store.LastSavedDocument);
        Assert.Null(saved.LastActiveConversationId);
        Assert.Equal(new[] { "session-2", "session-1" }, saved.Conversations.Select(c => c.ConversationId).ToArray());
        Assert.Equal("Newer", saved.Conversations[0].DisplayName);
        Assert.Equal(@"C:\repo\two", saved.Conversations[0].Cwd);
        Assert.Equal("remote-newer", saved.Conversations[0].RemoteSessionId);
        Assert.Equal("profile-newer", saved.Conversations[0].BoundProfileId);
        Assert.Single(saved.Conversations[0].Messages);
        Assert.Equal("newer", saved.Conversations[0].Messages[0].TextContent);
        Assert.Equal("Older", saved.Conversations[1].DisplayName);
        Assert.Equal(@"C:\repo\one", saved.Conversations[1].Cwd);
        Assert.Equal("remote-older", saved.Conversations[1].RemoteSessionId);
        Assert.Equal("profile-older", saved.Conversations[1].BoundProfileId);
        Assert.Single(saved.Conversations[1].Messages);
        Assert.Equal("older", saved.Conversations[1].Messages[0].TextContent);
    }

    [Fact]
    public async Task SaveAsync_DoesNotPersistSemanticVisibleSelection()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
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

        await workspace.TrySwitchToSessionAsync("session-1");
        await workspace.SaveAsync();

        var saved = Assert.IsType<ConversationDocument>(store.LastSavedDocument);
        Assert.Null(saved.LastActiveConversationId);
    }

    [Fact]
    public async Task RenameConversation_AdvancesConversationListVersionSoNavigationCanRefresh()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var session = await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        session.DisplayName = "Old Name";

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
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

        var beforeVersion = workspace.ConversationListVersion;

        workspace.RenameConversation("session-1", "Renamed Session");

        Assert.Equal(beforeVersion + 1, workspace.ConversationListVersion);
        var catalog = Assert.Single(workspace.GetCatalog());
        Assert.Equal("Renamed Session", catalog.DisplayName);
        Assert.Equal("Renamed Session", sessionManager.GetSession("session-1")!.DisplayName);
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
            return new AppPreferencesViewModel(
                appSettingsService.Object,
                startupService.Object,
                languageService.Object,
                capabilities.Object,
                uiRuntime.Object,
                Mock.Of<ILogger<AppPreferencesViewModel>>());
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
            ContentType = "text",
            TextContent = text,
            Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private sealed class CapturingConversationStore : IConversationStore
    {
        public ConversationDocument LoadResult { get; set; } = new();

        public ConversationDocument? LastSavedDocument { get; private set; }

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
}
