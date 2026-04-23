using System;
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
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class WorkspaceWriterTests
{
    [Fact]
    public async Task FlushAsync_HydratedConversation_DoesNotAdvanceLastUpdatedAt()
    {
        var dispatcher = new ImmediateUiDispatcher();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(dispatcher);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, dispatcher);
        using var writer = new WorkspaceWriter(workspace, dispatcher, TimeSpan.Zero);

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
        var dispatcher = new ImmediateUiDispatcher();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(dispatcher);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, dispatcher);
        using var writer = new WorkspaceWriter(workspace, dispatcher, TimeSpan.Zero);

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
                    false,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    null,
                    null)),
            Generation: 1), scheduleSave: false);
        await writer.FlushAsync();

        var snapshot = workspace.GetConversationSnapshot("session-bg");
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Transcript);
        Assert.Equal("background", snapshot.Transcript[0].TextContent);
        Assert.Equal("agent", snapshot.SelectedModeId);
    }

    [Fact]
    public async Task FlushAsync_BackgroundConversationSlices_PreserveProtocolMessageId()
    {
        var dispatcher = new ImmediateUiDispatcher();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(dispatcher);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, dispatcher);
        using var writer = new WorkspaceWriter(workspace, dispatcher, TimeSpan.Zero);

        var message = CreateTextMessage("bg-1", "background");
        message.ProtocolMessageId = "protocol-bg-1";

        writer.Enqueue(new ChatState(
            HydratedConversationId: "session-active",
            ConversationContents: ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "session-bg",
                new ConversationContentSlice(
                    ImmutableList.Create(message),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null)),
            Generation: 1), scheduleSave: false);
        await writer.FlushAsync();

        var snapshot = workspace.GetConversationSnapshot("session-bg");
        Assert.NotNull(snapshot);
        var persistedMessage = Assert.Single(snapshot!.Transcript);
        Assert.Equal("protocol-bg-1", persistedMessage.ProtocolMessageId);
    }

    [Fact]
    public async Task FlushAsync_BackgroundAuxiliarySessionState_DoesNotOverwriteExistingTranscriptAndPlan()
    {
        var dispatcher = new ImmediateUiDispatcher();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(dispatcher);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, dispatcher);
        using var writer = new WorkspaceWriter(workspace, dispatcher, TimeSpan.Zero);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-bg",
            Transcript:
            [
                CreateTextMessage("bg-1", "persisted transcript")
            ],
            Plan:
            [
                new ConversationPlanEntrySnapshot
                {
                    Content = "persisted plan",
                    Status = PlanEntryStatus.InProgress,
                    Priority = PlanEntryPriority.High
                }
            ],
            ShowPlanPanel: true,
            PlanTitle: "Persisted plan title",
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc)));

        writer.Enqueue(new ChatState(
            HydratedConversationId: "session-active",
            ConversationSessionStates: ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "session-bg",
                new ConversationSessionStateSlice(
                    ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false,
                    ImmutableList.Create(new ConversationAvailableCommandSnapshot("plan", "Planning command", "goal")),
                    new ConversationSessionInfoSnapshot
                    {
                        Title = "Background title",
                        Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["source"] = "store"
                        }
                    },
                    new ConversationUsageSnapshot(
                        9,
                        256,
                        new ConversationUsageCostSnapshot(2.5m, "USD")))),
            Generation: 1), scheduleSave: false);
        await writer.FlushAsync();

        var snapshot = workspace.GetConversationSnapshot("session-bg");
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Transcript);
        Assert.Equal("persisted transcript", snapshot.Transcript[0].TextContent);
        Assert.Single(snapshot.Plan);
        Assert.Equal("persisted plan", snapshot.Plan[0].Content);
        Assert.True(snapshot.ShowPlanPanel);
        Assert.Equal("Persisted plan title", snapshot.PlanTitle);
        var command = Assert.Single(snapshot.AvailableCommands ?? Array.Empty<ConversationAvailableCommandSnapshot>());
        Assert.Equal("plan", command.Name);
        Assert.NotNull(snapshot.SessionInfo);
        Assert.Equal("Background title", snapshot.SessionInfo!.Title);
        Assert.Equal("store", snapshot.SessionInfo.Meta!["source"]);
        Assert.NotNull(snapshot.Usage);
        Assert.Equal(9, snapshot.Usage!.Used);
    }

    [Fact]
    public async Task FlushAsync_HydratedConversationWithoutProjectedSessionInfo_PreservesExistingSessionInfoAuthority()
    {
        var dispatcher = new ImmediateUiDispatcher();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(dispatcher);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, dispatcher);
        using var writer = new WorkspaceWriter(workspace, dispatcher, TimeSpan.Zero);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-0", "persisted transcript")
            ],
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Persisted title",
                Cwd = @"C:\repo\one"
            }));

        writer.Enqueue(new ChatState(
            HydratedConversationId: "session-1",
            ConversationContents: ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "session-1",
                new ConversationContentSlice(
                    ImmutableList.Create(CreateTextMessage("m-1", "fresh transcript")),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null)),
            Generation: 1), scheduleSave: false);
        await writer.FlushAsync();

        var snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Transcript);
        Assert.Equal("fresh transcript", snapshot.Transcript[0].TextContent);
        Assert.NotNull(snapshot.SessionInfo);
        Assert.Equal("Persisted title", snapshot.SessionInfo!.Title);
        Assert.Equal(@"C:\repo\one", snapshot.SessionInfo.Cwd);
    }

    [Fact]
    public async Task FlushAsync_BackgroundAuxiliarySessionState_PreservesExistingPrimarySessionState()
    {
        var dispatcher = new ImmediateUiDispatcher();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(dispatcher);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, dispatcher);
        using var writer = new WorkspaceWriter(workspace, dispatcher, TimeSpan.Zero);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-bg",
            Transcript: Array.Empty<ConversationMessageSnapshot>(),
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc),
            AvailableModes:
            [
                new ConversationModeOptionSnapshot
                {
                    ModeId = "agent",
                    ModeName = "Agent"
                }
            ],
            SelectedModeId: "agent",
            ConfigOptions:
            [
                new ConversationConfigOptionSnapshot
                {
                    Id = "mode",
                    Name = "Mode",
                    SelectedValue = "agent"
                }
            ],
            ShowConfigOptionsPanel: true));

        writer.Enqueue(new ChatState(
            HydratedConversationId: "session-active",
            ConversationSessionStates: ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "session-bg",
                new ConversationSessionStateSlice(
                    ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false,
                    ImmutableList.Create(new ConversationAvailableCommandSnapshot("plan", "Planning command", "goal")),
                    new ConversationSessionInfoSnapshot
                    {
                        Title = "Background title"
                    },
                    new ConversationUsageSnapshot(
                        9,
                        256,
                        new ConversationUsageCostSnapshot(2.5m, "USD")))),
            Generation: 1), scheduleSave: false);
        await writer.FlushAsync();

        var snapshot = workspace.GetConversationSnapshot("session-bg");
        Assert.NotNull(snapshot);
        var availableMode = Assert.Single(snapshot!.AvailableModes ?? Array.Empty<ConversationModeOptionSnapshot>());
        Assert.Equal("agent", availableMode.ModeId);
        Assert.Equal("agent", snapshot.SelectedModeId);
        var configOption = Assert.Single(snapshot.ConfigOptions ?? Array.Empty<ConversationConfigOptionSnapshot>());
        Assert.Equal("mode", configOption.Id);
        Assert.True(snapshot.ShowConfigOptionsPanel);
        var command = Assert.Single(snapshot.AvailableCommands ?? Array.Empty<ConversationAvailableCommandSnapshot>());
        Assert.Equal("plan", command.Name);
        Assert.Equal("Background title", snapshot.SessionInfo!.Title);
        Assert.Equal(9, snapshot.Usage!.Used);
    }

    [Fact]
    public async Task FlushAsync_BackgroundContentOnlyUpdate_PreservesExistingSessionState()
    {
        var dispatcher = new ImmediateUiDispatcher();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(dispatcher);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, dispatcher);
        using var writer = new WorkspaceWriter(workspace, dispatcher, TimeSpan.Zero);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-bg",
            Transcript:
            [
                CreateTextMessage("bg-1", "persisted transcript")
            ],
            Plan:
            [
                new ConversationPlanEntrySnapshot
                {
                    Content = "persisted plan",
                    Status = PlanEntryStatus.InProgress,
                    Priority = PlanEntryPriority.High
                }
            ],
            ShowPlanPanel: true,
            PlanTitle: "Persisted plan title",
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc),
            AvailableModes:
            [
                new ConversationModeOptionSnapshot
                {
                    ModeId = "agent",
                    ModeName = "Agent"
                }
            ],
            SelectedModeId: "agent",
            ConfigOptions:
            [
                new ConversationConfigOptionSnapshot
                {
                    Id = "mode",
                    Name = "Mode",
                    SelectedValue = "agent"
                }
            ],
            ShowConfigOptionsPanel: true,
            AvailableCommands:
            [
                new ConversationAvailableCommandSnapshot("plan", "Planning command", "goal")
            ],
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Background title"
            },
            Usage: new ConversationUsageSnapshot(
                9,
                256,
                new ConversationUsageCostSnapshot(2.5m, "USD"))));

        writer.Enqueue(new ChatState(
            HydratedConversationId: "session-active",
            ConversationContents: ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "session-bg",
                new ConversationContentSlice(
                    ImmutableList.Create(CreateTextMessage("bg-2", "fresh transcript")),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null)),
            Generation: 1), scheduleSave: false);
        await writer.FlushAsync();

        var snapshot = workspace.GetConversationSnapshot("session-bg");
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Transcript);
        Assert.Equal("fresh transcript", snapshot.Transcript[0].TextContent);
        var availableMode = Assert.Single(snapshot.AvailableModes ?? Array.Empty<ConversationModeOptionSnapshot>());
        Assert.Equal("agent", availableMode.ModeId);
        Assert.Equal("agent", snapshot.SelectedModeId);
        var configOption = Assert.Single(snapshot.ConfigOptions ?? Array.Empty<ConversationConfigOptionSnapshot>());
        Assert.Equal("mode", configOption.Id);
        Assert.True(snapshot.ShowConfigOptionsPanel);
        var command = Assert.Single(snapshot.AvailableCommands ?? Array.Empty<ConversationAvailableCommandSnapshot>());
        Assert.Equal("plan", command.Name);
        Assert.NotNull(snapshot.SessionInfo);
        Assert.Equal("Background title", snapshot.SessionInfo!.Title);
        Assert.NotNull(snapshot.Usage);
        Assert.Equal(9, snapshot.Usage!.Used);
    }

    private static ChatConversationWorkspace CreateWorkspace(
        IConversationStore store,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        IUiDispatcher uiDispatcher)
        => new(
            sessionManager,
            store,
            new AppPreferencesConversationWorkspacePreferences(preferences),
            Mock.Of<ILogger<ChatConversationWorkspace>>(),
            uiDispatcher);

    private static AppPreferencesViewModel CreatePreferences(IUiDispatcher uiDispatcher)
    {
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
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            uiDispatcher);
    }

    private static ConversationMessageSnapshot CreateTextMessage(string id, string text)
        => new()
        {
            Id = id,
            ContentType = "text",
            TextContent = text,
            Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };

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
