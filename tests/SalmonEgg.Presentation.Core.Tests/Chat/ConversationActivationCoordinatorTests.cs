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
    public async Task ActivateSessionAsync_HydratesSnapshot_WhenSwitchingSessions_EvenIfGenerationIsNonZero()
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
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.NotNull(currentState.Transcript);
        Assert.Single(currentState.Transcript!);
        Assert.Equal("hello", currentState.Transcript[0].TextContent);
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
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.Null(currentState.Binding);
    }

    [Fact]
    public async Task ActivateSessionAsync_HydratesSnapshot_WhenStoreAlreadyHasBindingData()
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
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.NotNull(currentState.Transcript);
        Assert.Single(currentState.Transcript!);
        Assert.Equal("hello", currentState.Transcript[0].TextContent);
    }

    [Fact]
    public async Task ActivateSessionAsync_DoesNotHydrate_WhenConversationContentSliceAlreadyHasData()
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
                CreateTextMessage("workspace-1", "workspace")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var state = State.Value(new object(), () => ChatState.Empty with
        {
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "session-1",
                new ConversationContentSlice(
                    ImmutableList.Create(CreateTextMessage("cached-1", "cached")),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null))
        });
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.NotNull(currentState.Transcript);
        Assert.Single(currentState.Transcript!);
        Assert.Equal("cached", currentState.Transcript[0].TextContent);
    }

    [Fact]
    public async Task ActivateSessionAsync_HydratesWorkspaceContent_WhenOnlyAuxiliarySessionStateIsProjected()
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
                CreateTextMessage("workspace-1", "workspace transcript")
            ],
            Plan:
            [
                new ConversationPlanEntrySnapshot
                {
                    Content = "workspace plan",
                    Status = PlanEntryStatus.InProgress,
                    Priority = PlanEntryPriority.High
                }
            ],
            ShowPlanPanel: true,
            PlanTitle: "workspace title",
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var state = State.Value(new object(), () => ChatState.Empty with
        {
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "session-1",
                new ConversationSessionStateSlice(
                    ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false,
                    ImmutableList.Create(new ConversationAvailableCommandSnapshot("plan", "Planning command", "goal")),
                    new ConversationSessionInfoSnapshot
                    {
                        Title = "store title",
                        Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["source"] = "store"
                        }
                    },
                    new ConversationUsageSnapshot(
                        5,
                        128,
                        new ConversationUsageCostSnapshot(1.5m, "USD"))))
        });
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.NotNull(currentState.Transcript);
        Assert.Single(currentState.Transcript!);
        Assert.Equal("workspace transcript", currentState.Transcript[0].TextContent);
        Assert.NotNull(currentState.PlanEntries);
        Assert.Single(currentState.PlanEntries!);
        Assert.Equal("workspace plan", currentState.PlanEntries[0].Content);
        Assert.True(currentState.ShowPlanPanel);
        Assert.Equal("workspace title", currentState.PlanTitle);

        var sessionState = currentState.ResolveSessionStateSlice("session-1");
        Assert.NotNull(sessionState);
        var command = Assert.Single(sessionState!.Value.AvailableCommands);
        Assert.Equal("plan", command.Name);
        Assert.NotNull(sessionState.Value.SessionInfo);
        Assert.Equal("store title", sessionState.Value.SessionInfo!.Title);
        Assert.NotNull(sessionState.Value.Usage);
        Assert.Equal(5, sessionState.Value.Usage!.Used);
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
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.NotNull(currentState.Transcript);
        Assert.Single(currentState.Transcript!);
        Assert.NotNull(currentState.PlanEntries);
        Assert.Single(currentState.PlanEntries!);
        Assert.Null(currentState.Binding);
    }

    [Fact]
    public async Task ActivateSessionAsync_HydratesMissingAuxiliarySessionState_FromWorkspaceSnapshot()
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
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            AvailableCommands:
            [
                new ConversationAvailableCommandSnapshot("plan", "Planning command", "goal")
            ],
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "workspace title"
            },
            Usage: new ConversationUsageSnapshot(3, 42, new ConversationUsageCostSnapshot(1.25m, "USD"))));
        workspace.UpdateRemoteBinding("session-1", "remote-1", "profile-a");

        var state = State.Value(new object(), () => ChatState.Empty with
        {
            HydratedConversationId = "session-1",
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "session-1",
                new ConversationSessionStateSlice(
                    ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent", Description = "Agent mode" }),
                    "agent",
                    ImmutableList.Create(new ConversationConfigOptionSnapshot { Id = "mode", Name = "Mode", SelectedValue = "agent" }),
                    true,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    null,
                    null))
        });
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = Assert.IsType<ChatState>(await state);
        var sessionState = Assert.NotNull(currentState.ResolveSessionStateSlice("session-1"));
        Assert.Equal("agent", sessionState.SelectedModeId);
        var command = Assert.Single(sessionState.AvailableCommands);
        Assert.Equal("plan", command.Name);
        Assert.NotNull(sessionState.SessionInfo);
        Assert.Equal("workspace title", sessionState.SessionInfo!.Title);
        Assert.NotNull(sessionState.Usage);
        Assert.Equal(3, sessionState.Usage!.Used);
    }

    [Fact]
    public async Task ActivateSessionAsync_HydratesPrimarySessionState_WhenOnlyAuxiliarySessionStateIsProjected()
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
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            AvailableModes:
            [
                new ConversationModeOptionSnapshot
                {
                    ModeId = "agent",
                    ModeName = "Agent",
                    Description = "Agent mode"
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

        var state = State.Value(new object(), () => ChatState.Empty with
        {
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "session-1",
                new ConversationSessionStateSlice(
                    ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false,
                    ImmutableList.Create(new ConversationAvailableCommandSnapshot("plan", "Planning command", "goal")),
                    new ConversationSessionInfoSnapshot
                    {
                        Title = "store title"
                    },
                    new ConversationUsageSnapshot(
                        5,
                        128,
                        new ConversationUsageCostSnapshot(1.5m, "USD"))))
        });
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        var currentState = Assert.IsType<ChatState>(await state);
        var sessionState = Assert.NotNull(currentState.ResolveSessionStateSlice("session-1"));
        var availableMode = Assert.Single(sessionState.AvailableModes);
        Assert.Equal("agent", availableMode.ModeId);
        Assert.Equal("agent", sessionState.SelectedModeId);
        var configOption = Assert.Single(sessionState.ConfigOptions);
        Assert.Equal("mode", configOption.Id);
        Assert.True(sessionState.ShowConfigOptionsPanel);
        var command = Assert.Single(sessionState.AvailableCommands);
        Assert.Equal("plan", command.Name);
        Assert.NotNull(sessionState.SessionInfo);
        Assert.Equal("store title", sessionState.SessionInfo!.Title);
        Assert.NotNull(sessionState.Usage);
        Assert.Equal(5, sessionState.Usage!.Used);
    }

    [Fact]
    public async Task ActivateSessionAsync_SelectionOnly_SkipsWorkspaceSnapshotHydration()
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

        var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync(
            "session-1",
            ConversationActivationHydrationMode.SelectionOnly);

        Assert.True(result.Succeeded);
        var currentState = await WaitForStateAsync(
            state,
            current => string.Equals(current?.HydratedConversationId, "session-1", StringComparison.Ordinal));
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.Null(currentState.Transcript);
        Assert.Null(currentState.PlanEntries);
        Assert.False(currentState.ShowPlanPanel);
        Assert.Null(currentState.PlanTitle);
    }

    [Fact]
    public async Task ActivateSessionAsync_MetadataOnly_RestoresSessionInfoWithoutHydratingWorkspaceTranscript()
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
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            AvailableModes:
            [
                new ConversationModeOptionSnapshot
                {
                    ModeId = "agent",
                    ModeName = "Agent",
                    Description = "Agent mode"
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
                new ConversationAvailableCommandSnapshot("plan", "Planning command", "target")
            ],
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Remote title",
                Cwd = @"C:\repo\one"
            },
            Usage: new ConversationUsageSnapshot(
                3,
                99,
                new ConversationUsageCostSnapshot(1.25m, "USD"))));

        var state = State.Value(new object(), () => ChatState.Empty);
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync(
            "session-1",
            ConversationActivationHydrationMode.MetadataOnly);

        Assert.True(result.Succeeded);
        var currentState = await WaitForStateAsync(
            state,
            current => string.Equals(current?.HydratedConversationId, "session-1", StringComparison.Ordinal));
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.Null(currentState.Transcript);
        Assert.Null(currentState.PlanEntries);
        Assert.False(currentState.ShowPlanPanel);
        Assert.Null(currentState.PlanTitle);
        Assert.NotNull(currentState.SessionInfo);
        Assert.Equal("Remote title", currentState.SessionInfo!.Title);
        Assert.Equal(@"C:\repo\one", currentState.SessionInfo.Cwd);
        Assert.True(currentState.AvailableModes is null || currentState.AvailableModes.Count == 0);
        Assert.Null(currentState.SelectedModeId);
        Assert.True(currentState.ConfigOptions is null || currentState.ConfigOptions.Count == 0);
        Assert.True(currentState.AvailableCommands is null || currentState.AvailableCommands.Count == 0);
        Assert.Null(currentState.Usage);
    }

    [Fact]
    public async Task ActivateSessionAsync_CommitsVisibleShellSelection_OnlyAfterActivationSucceeds()
    {
        var activationGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var selectionStore = new ShellSelectionStateStore();
        var projectSelectionStore = new RecordingProjectSelectionStore();
        var coordinator = new NavigationCoordinator(
            selectionStore,
            new ShellNavigationRuntimeStateStore(),
            new ControlledConversationSessionSwitcher(activationGate.Task),
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
    public async Task ActivateSessionAsync_ProfileMismatch_PreservesRemoteBinding_AndSwitchesSelectedProfileToConversationBinding()
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
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-1");

        Assert.True(result.Succeeded);
        Assert.Equal("session-1", result.ConversationId);
        var currentState = Assert.IsType<ChatState>(await state);
        Assert.Equal("session-1", currentState.HydratedConversationId);
        Assert.Equal(new ConversationBindingSlice("session-1", "remote-1", "profile-a"), currentState.Binding);

        var workspaceBinding = workspace.GetRemoteBinding("session-1");
        Assert.NotNull(workspaceBinding);
        Assert.Equal("profile-a", workspaceBinding!.BoundProfileId);
        Assert.Equal("remote-1", workspaceBinding.RemoteSessionId);

        var currentConnectionState = await connectionStore.State;
        Assert.Equal("profile-a", currentConnectionState!.SettingsSelectedProfileId);
    }

    [Fact]
    public async Task ActivateSessionAsync_ProfileMismatch_DoesNotReorderCatalog()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-old", @"C:\repo\one");
        await sessionManager.CreateSessionAsync("session-new", @"C:\repo\one");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-old",
            Transcript:
            [
                CreateTextMessage("m-old", "older")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc)));
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-new",
            Transcript:
            [
                CreateTextMessage("m-new", "newer")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 2, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 3, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-old", "remote-1", "profile-a");

        var versionBeforeActivation = workspace.ConversationListVersion;
        var state = State.Value(new object(), () => ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "session-old",
                new ConversationBindingSlice("session-old", "remote-1", "profile-a"))
        });
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore("profile-b");
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ActivateSessionAsync("session-old");

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "session-new", "session-old" }, workspace.GetKnownConversationIds());
        Assert.Equal(versionBeforeActivation, workspace.ConversationListVersion);
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
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ArchiveConversationAsync("session-1", "session-1");

        Assert.True(result.Succeeded);
        Assert.True(result.ClearedActiveConversation);
        Assert.DoesNotContain("session-1", workspace.GetKnownConversationIds());
        var currentState = await WaitForStateAsync(state, current => current?.HydratedConversationId is null);
        Assert.Null(currentState.HydratedConversationId);
    }

    [Fact]
    public async Task ArchiveConversation_WhenWorkspaceMutationFails_DoesNotClearVisibleSelection()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
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
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        workspace.Dispose();

        var result = await coordinator.ArchiveConversationAsync("session-1", "session-1");

        Assert.False(result.Succeeded);
        Assert.False(result.ClearedActiveConversation);
        var currentState = await state ?? ChatState.Empty;
        Assert.Equal("session-1", currentState.HydratedConversationId);
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
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.DeleteConversationAsync("session-1", "session-1");

        Assert.True(result.Succeeded);
        Assert.True(result.ClearedActiveConversation);
        Assert.DoesNotContain("session-1", workspace.GetKnownConversationIds());
        var currentState = await WaitForStateAsync(state, current => current?.HydratedConversationId is null);
        Assert.Null(currentState.HydratedConversationId);
    }

    [Fact]
    public async Task ArchiveConversation_ClearsBindingSlice_ToPreventArchivedConversationResurrection()
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
        await state.Update(
            current => ChatReducer.Reduce(
                current,
                new SetBindingSliceAction(new ConversationBindingSlice("session-1", "remote-1", "profile-1"))),
            default);
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ArchiveConversationAsync("session-1", activeConversationId: null);

        Assert.True(result.Succeeded);
        var currentState = await state ?? ChatState.Empty;
        Assert.Null(currentState.ResolveBinding("session-1"));
    }

    [Fact]
    public async Task DeleteConversation_ClearsBindingSlice_ToPreventDeletedConversationResurrection()
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
        await state.Update(
            current => ChatReducer.Reduce(
                current,
                new SetBindingSliceAction(new ConversationBindingSlice("session-1", "remote-1", "profile-1"))),
            default);
        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindingCommands = new BindingCoordinator(workspace, chatStore);
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindingCommands,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.DeleteConversationAsync("session-1", activeConversationId: null);

        Assert.True(result.Succeeded);
        var currentState = await state ?? ChatState.Empty;
        Assert.Null(currentState.ResolveBinding("session-1"));
    }

    [Fact]
    public async Task ArchiveConversation_WhenBindingClearFails_DoesNotRemoveConversation()
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

        var chatStore = CreateChatStore(State.Value(new object(), () => ChatState.Empty));
        var connectionStore = CreateConnectionStore();
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            new FailedBindingCommands(),
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.ArchiveConversationAsync("session-1", activeConversationId: null);

        Assert.False(result.Succeeded);
        Assert.Equal("forced-binding-clear-failure", result.FailureReason);
        Assert.Contains("session-1", workspace.GetKnownConversationIds());
    }

    [Fact]
    public async Task DeleteConversation_WhenRemoveThrows_RestoresBindingAndSelection()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        workspace.Dispose();

        var state = State.Value(new object(), () => ChatState.Empty);
        await state.Update(
            current => ChatReducer.Reduce(
                ChatReducer.Reduce(
                    current,
                    new SetBindingSliceAction(new ConversationBindingSlice("session-1", "remote-1", "profile-1"))),
                new SelectConversationAction("session-1")),
            default);

        var chatStore = CreateChatStore(state);
        var connectionStore = CreateConnectionStore();
        var bindings = new RecordingBindingCommands();
        var coordinator = new ConversationActivationCoordinator(
            workspace,
            bindings,
            chatStore,
            connectionStore,
            Mock.Of<ILogger<ConversationActivationCoordinator>>());

        var result = await coordinator.DeleteConversationAsync("session-1", "session-1");

        Assert.False(result.Succeeded);
        Assert.True(bindings.ClearedCalled);
        Assert.True(bindings.RestoreCalled);
        Assert.Equal(("session-1", "remote-1", "profile-1"), bindings.LastRestore);
        var finalState = await state ?? ChatState.Empty;
        Assert.Equal("session-1", finalState.HydratedConversationId);
        Assert.NotNull(finalState.ResolveBinding("session-1"));
    }

    private static IChatStore CreateChatStore(IState<ChatState> state)
    {
        return new ChatStore(state);
    }

    private static IChatConnectionStore CreateConnectionStore(string? selectedProfileId = null)
    {
        var state = State.Value(new object(), () => ChatConnectionState.Empty with
        {
            ForegroundTransportProfileId = selectedProfileId
        });
        return new ChatConnectionStore(state);
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

    private static ConversationMessageSnapshot CreateTextMessage(string id, string text)
        => new()
        {
            Id = id,
            Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            IsOutgoing = false,
            ContentType = "text",
            TextContent = text
        };

    private static async Task<ChatState> WaitForStateAsync(
        IState<ChatState> state,
        Func<ChatState?, bool> predicate,
        int maxAttempts = 20,
        int delayMs = 10)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var current = await state ?? ChatState.Empty;
            if (predicate(current))
            {
                return current;
            }

            await Task.Delay(delayMs);
        }

        return await state ?? ChatState.Empty;
    }

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

    private sealed class ControlledConversationSessionSwitcher : IConversationSessionSwitcher
    {
        private readonly Task<bool> _activationTask;

        public ControlledConversationSessionSwitcher(Task<bool> activationTask)
        {
            _activationTask = activationTask;
        }

        public Task<bool> SwitchConversationAsync(string conversationId, CancellationToken cancellationToken = default)
            => _activationTask.WaitAsync(cancellationToken);
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

        public ValueTask<ShellNavigationResult> NavigateToDiscoverSessions()
            => ValueTask.FromResult(ShellNavigationResult.Success());
    }

    private sealed class FailedBindingCommands : IConversationBindingCommands
    {
        public ValueTask<BindingUpdateResult> UpdateBindingAsync(
            string conversationId,
            string? remoteSessionId,
            string? profileId)
            => ValueTask.FromResult(new BindingUpdateResult(BindingUpdateStatus.Success, null));

        public ValueTask<BindingUpdateResult> ClearBindingAsync(string conversationId)
            => ValueTask.FromResult(new BindingUpdateResult(BindingUpdateStatus.Error, "forced-binding-clear-failure"));
    }

    private sealed class RecordingBindingCommands : IConversationBindingCommands
    {
        public bool ClearedCalled { get; private set; }

        public bool RestoreCalled { get; private set; }

        public (string ConversationId, string? RemoteSessionId, string? ProfileId)? LastRestore { get; private set; }

        public ValueTask<BindingUpdateResult> UpdateBindingAsync(
            string conversationId,
            string? remoteSessionId,
            string? profileId)
        {
            if (string.IsNullOrWhiteSpace(remoteSessionId) && string.IsNullOrWhiteSpace(profileId))
            {
                ClearedCalled = true;
                return ValueTask.FromResult(BindingUpdateResult.Success());
            }

            RestoreCalled = true;
            LastRestore = (conversationId, remoteSessionId, profileId);
            return ValueTask.FromResult(BindingUpdateResult.Success());
        }
    }
}
