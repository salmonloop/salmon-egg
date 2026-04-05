using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class WorkspaceWriterTests
{
    [Fact]
    public async Task FlushAsync_HydratedConversation_DoesNotAdvanceLastUpdatedAt()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        using var writer = new WorkspaceWriter(workspace, TimeSpan.Zero, syncContext);

        var originalUpdatedAt = new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc);
        var message = CreateTextMessage("m-1", "hello");
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                message
            ],
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: originalUpdatedAt));

        writer.Enqueue(new ChatState(
            HydratedConversationId: "session-1",
            ConversationContents: ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "session-1",
                new ConversationContentSlice(
                    ImmutableList<ConversationMessageSnapshot>.Empty.Add(message),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null)),
            Generation: 1), scheduleSave: false);
        await writer.FlushAsync();

        var snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(snapshot);
        Assert.Equal(originalUpdatedAt, snapshot!.LastUpdatedAt);
    }

    [Fact]
    public async Task FlushAsync_BackgroundConversationSlices_ArePersistedToWorkspace()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        using var writer = new WorkspaceWriter(workspace, TimeSpan.Zero, syncContext);

        writer.Enqueue(new ChatState(
            HydratedConversationId: "session-active",
            ConversationContents: ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "session-bg",
                new ConversationContentSlice(
                    ImmutableList.Create(CreateTextMessage("bg-1", "background")),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null)),
            ConversationSessionStates: ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "session-bg",
                new ConversationSessionStateSlice(
                    ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" }),
                    "agent",
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false)),
            Generation: 1), scheduleSave: false);
        await writer.FlushAsync();

        var snapshot = workspace.GetConversationSnapshot("session-bg");
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Transcript);
        Assert.Equal("background", snapshot.Transcript[0].TextContent);
        Assert.Equal("agent", snapshot.SelectedModeId);
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
        public Task<ConversationDocument> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ConversationDocument());

        public Task SaveAsync(ConversationDocument document, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeSessionManager : ISessionManager
    {
        public Task<Session> CreateSessionAsync(string sessionId, string? cwd = null)
            => Task.FromResult(new Session(sessionId, cwd));

        public Session? GetSession(string sessionId) => null;

        public bool UpdateSession(string sessionId, Action<Session> updateAction, bool updateActivity = true) => false;

        public Task<bool> CancelSessionAsync(string sessionId, string? reason = null)
            => Task.FromResult(false);

        public System.Collections.Generic.IEnumerable<Session> GetAllSessions()
            => Array.Empty<Session>();

        public bool RemoveSession(string sessionId) => false;
    }
}
