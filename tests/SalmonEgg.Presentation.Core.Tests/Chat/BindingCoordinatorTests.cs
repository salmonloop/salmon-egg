using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Acp.Plan;
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
using SalmonEgg.Presentation.Core.Tests.Localization;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class BindingCoordinatorTests
{
    [Theory]
    [InlineData("profile-old", "remote-old", true)]
    [InlineData("profile-new", "remote-new", false)]
    public async Task UpdateBinding_ReconcilesUnreadOnlyWhenItsLogicalSessionChanged(
        string attentionProfile, string attentionRemote, bool shouldRemove)
    {
        var syncContext = new ImmediateSynchronizationContext();
        using var workspace = CreateWorkspace(new CapturingConversationStore(), new FakeSessionManager(), CreatePreferences(syncContext), syncContext);
        await using var state = State.Value(new object(), () => ChatState.Empty);
        await using var attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
        var attention = new ConversationAttentionStore(attentionState);
        var chatStore = new ChatStore(state);
        var content = new ConversationMessageSnapshot { Id = "reply", ContentType = "text", TextContent = "reply" };
        await attention.Dispatch(new MarkConversationUnreadAction("conversation", ConversationAttentionSource.AgentMessage,
            DateTime.UtcNow, attentionProfile, attentionRemote, content, "connection"));
        var coordinator = new BindingCoordinator(workspace, chatStore, attention);

        var result = await coordinator.UpdateBindingAsync("conversation", "remote-new", "profile-new");

        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        var current = await attention.GetCurrentStateAsync();
        Assert.Equal(!shouldRemove, current.TryGetConversation("conversation", out var remaining));
        if (!shouldRemove)
        {
            Assert.True(remaining!.HasUnread);
            Assert.Same(content, remaining.Content);
        }
    }

    [Fact]
    public async Task UpdateBinding_UpdatesStoreSlice_AndPersistsWorkspaceBinding()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        await workspace.RestoreAsync(TestContext.Current.CancellationToken);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-old", "profile-old");

        var initialState = ChatState.Empty;
        var state = State.Value(this, () => initialState);
        var chatStore = CreateChatStore(state, initialState);
        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.UpdateBindingAsync("session-1", "remote-new", "profile-new");

        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        var currentState = await chatStore.Object.GetCurrentStateAsync();
        Assert.Equal(
            new ConversationBindingSlice("session-1", "remote-new", "profile-new"),
            currentState.ResolveBinding("session-1"));

        var workspaceBinding = workspace.GetRemoteBinding("session-1");
        Assert.NotNull(workspaceBinding);
        Assert.Equal("remote-new", workspaceBinding!.RemoteSessionId);
        Assert.Equal("profile-new", workspaceBinding.BoundProfileId);
    }

    [Fact]
    public async Task UpdateBinding_WhenRemoteSessionIdIsReboundWithinProfile_ClearsPreviousOwner()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        await sessionManager.CreateSessionAsync("session-2", @"C:\repo\two");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        await workspace.RestoreAsync(TestContext.Current.CancellationToken);
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
                    Status = PlanEntryStatus.Pending.ToString(),
                    Priority = PlanEntryPriority.Medium.ToString()
                }
            ],
            ShowPlanPanel: true,
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
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-shared", "profile-2");
        workspace.UpdateRemoteBinding("session-2", "remote-old", "profile-2");

        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "session-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("session-1", new ConversationBindingSlice("session-1", "remote-shared", "profile-2"))
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
                        Status = PlanEntryStatus.Pending.ToString(),
                        Priority = PlanEntryPriority.Medium.ToString()
                    }),
                    true)),
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
                    "profile-2",
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
                Status = PlanEntryStatus.Pending.ToString(),
                Priority = PlanEntryPriority.Medium.ToString()
            }),
            AvailableModes = ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "mode-1", ModeName = "Mode 1" }),
            SelectedModeId = "mode-1",
            ConfigOptions = ImmutableList.Create(new ConversationConfigOptionSnapshot { Id = "cfg-1", Name = "Config 1", SelectedValue = "value" }),
            ShowConfigOptionsPanel = true,
            AvailableCommands = ImmutableList.Create(new ConversationAvailableCommandSnapshot("cmd-1", "Command 1", "desc")),
            SessionInfo = new ConversationSessionInfoSnapshot { Title = "Remote title", Cwd = @"C:\repo\one" },
            Usage = new ConversationUsageSnapshot { Used = 1, Size = 2 },
            ShowPlanPanel = true
        };
        var state = State.Value(this, () => initialState);
        var chatStore = CreateChatStore(state, initialState);
        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.UpdateBindingAsync("session-2", "remote-shared", "profile-2");

        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        var currentState = await chatStore.Object.GetCurrentStateAsync();
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

        var workspaceBinding2 = workspace.GetRemoteBinding("session-2");
        Assert.NotNull(workspaceBinding2);
        Assert.Equal("remote-shared", workspaceBinding2!.RemoteSessionId);
        Assert.Equal("profile-2", workspaceBinding2.BoundProfileId);
    }

    [Fact]
    public async Task UpdateBinding_WhenDifferentProfilesReuseRemoteId_PreservesOtherProfileBindingAndContent()
    {
        // Arrange
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("a", @"C:\repo\a");
        await sessionManager.CreateSessionAsync("b", @"C:\repo\b");
        using var workspace = CreateWorkspace(new CapturingConversationStore(), sessionManager, preferences, syncContext);
        await workspace.RestoreAsync(TestContext.Current.CancellationToken);
        workspace.UpdateRemoteBinding("a", "shared", "profile-a");
        var content = new ConversationContentSlice(
            ImmutableList.Create(new ConversationMessageSnapshot { Id = "message", TextContent = "Keep A" }),
            ImmutableList<ConversationPlanEntrySnapshot>.Empty, false);
        var initial = ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add("a", new("a", "shared", "profile-a")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add("a", content)
        };
        var state = State.Value(this, () => initial);
        var store = CreateChatStore(state, initial);
        var coordinator = new BindingCoordinator(workspace, store.Object);

        // Act
        var result = await coordinator.UpdateBindingAsync("b", "shared", "profile-b");

        // Assert
        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        var current = await store.Object.GetCurrentStateAsync();
        Assert.Equal("profile-a", current.ResolveBinding("a")?.ProfileId);
        Assert.Equal("profile-b", current.ResolveBinding("b")?.ProfileId);
        Assert.Equal(content, current.ResolveContentSlice("a"));
        Assert.Equal("shared", workspace.GetRemoteBinding("a")?.RemoteSessionId);
    }

    [Fact]
    public async Task UpdateBinding_WhenSameConversationMovesProfileWithSameRemoteId_ScrubsOldAuthority()
    {
        // Arrange
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("conversation", @"C:\repo");
        using var workspace = CreateWorkspace(new CapturingConversationStore(), sessionManager, preferences, syncContext);
        await workspace.RestoreAsync(TestContext.Current.CancellationToken);
        workspace.UpdateRemoteBinding("conversation", "same-remote", "old-profile");
        var initial = ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add("conversation", new("conversation", "same-remote", "old-profile")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add("conversation", new(
                ImmutableList.Create(new ConversationMessageSnapshot { Id = "old", TextContent = "Old agent content" }),
                ImmutableList<ConversationPlanEntrySnapshot>.Empty, false)),
            OperationFailures = ImmutableDictionary<string, ConversationOperationFailure>.Empty.Add("conversation", new("conversation", "Old agent fault"))
        };
        var store = CreateChatStore(State.Value(this, () => initial), initial);
        var coordinator = new BindingCoordinator(workspace, store.Object);

        // Act
        var result = await coordinator.UpdateBindingAsync("conversation", "same-remote", "new-profile");

        // Assert
        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        var current = await store.Object.GetCurrentStateAsync();
        Assert.Equal("new-profile", current.ResolveBinding("conversation")?.ProfileId);
        Assert.Empty(current.ResolveContentSlice("conversation")!.Value.Transcript);
        Assert.Null(current.ResolveOperationFailure("conversation"));
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
        await workspace.RestoreAsync(TestContext.Current.CancellationToken);
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
                CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                SessionInfo: new ConversationSessionInfoSnapshot { Title = "Remote title", Cwd = @"C:\repo\one" }),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        workspace.UpdateRemoteBinding("session-1", "remote-old", "profile-old");

        var initialState = ChatState.Empty with
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
                    false)),
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
        };
        var state = State.Value(this, () => initialState);
        var chatStore = CreateChatStore(state, initialState);
        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.ClearBindingAsync("session-1");

        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        var currentState = await chatStore.Object.GetCurrentStateAsync();
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
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-old", "profile-old");

        var state = State.Value(this, () => ChatState.Empty);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.GetCurrentStateAsync()).ReturnsAsync(ChatState.Empty);
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
    public async Task UpdateBinding_UsesAuthoritativeStoreSnapshotNotReactiveProjection()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var workspaceStore = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        using var workspace = CreateWorkspace(workspaceStore, sessionManager, preferences, syncContext);
        await workspace.RestoreAsync(TestContext.Current.CancellationToken);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var state = State.Value(this, () => ChatState.Empty);
        var authoritativeState = ChatState.Empty;
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.GetCurrentStateAsync())
            .ReturnsAsync(() => authoritativeState);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action =>
            {
                authoritativeState = ChatReducer.Reduce(authoritativeState, action);
                return ValueTask.CompletedTask;
            });

        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.UpdateBindingAsync("session-1", "remote-new", "profile-new");

        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        Assert.Equal(
            new ConversationBindingSlice("session-1", "remote-new", "profile-new"),
            authoritativeState.ResolveBinding("session-1"));
        Assert.Null((await state)?.ResolveBinding("session-1"));
        chatStore.Verify(s => s.GetCurrentStateAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpdateBinding_WhenDispatchDoesNotCommitAuthoritativeState_ReturnsStoreMismatch()
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
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)));

        var state = State.Value(this, () => ChatState.Empty);
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.GetCurrentStateAsync()).ReturnsAsync(ChatState.Empty);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns(ValueTask.CompletedTask);

        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.UpdateBindingAsync("session-1", "remote-new", "profile-new");

        Assert.Equal(BindingUpdateStatus.Error, result.Status);
        Assert.Equal("BindingStoreMismatch", result.ErrorMessage);
        var workspaceBinding = workspace.GetRemoteBinding("session-1");
        Assert.NotNull(workspaceBinding);
        Assert.Null(workspaceBinding!.RemoteSessionId);
        Assert.Null(workspaceBinding.BoundProfileId);
    }

    [Fact]
    public async Task UpdateBinding_WhenFinalBindingMismatch_DoesNotPartiallyMutateWorkspace()
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
                    Status = PlanEntryStatus.Pending.ToString(),
                    Priority = PlanEntryPriority.Medium.ToString()
                }
            ],
            ShowPlanPanel: true,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-2",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-shared", "profile-1");
        workspace.UpdateRemoteBinding("session-2", "remote-old", "profile-2");

        var initialState = ChatState.Empty with
        {
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
                        Status = PlanEntryStatus.Pending.ToString(),
                        Priority = PlanEntryPriority.Medium.ToString()
                    }),
                    true))
        };
        var state = State.Value(this, () => initialState);
        var authoritativeState = initialState;
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.GetCurrentStateAsync())
            .ReturnsAsync(() => authoritativeState);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(action =>
            {
                if (action is not ApplyBindingUpdateAction)
                {
                    authoritativeState = ChatReducer.Reduce(authoritativeState, action);
                }

                return ValueTask.CompletedTask;
            });
        var coordinator = new BindingCoordinator(workspace, chatStore.Object);

        var result = await coordinator.UpdateBindingAsync("session-2", "remote-shared", "profile-2");

        Assert.Equal(BindingUpdateStatus.Error, result.Status);
        Assert.Equal("BindingStoreMismatch", result.ErrorMessage);
        Assert.Equal("remote-shared", authoritativeState.ResolveBinding("session-1")?.RemoteSessionId);
        Assert.Equal("profile-1", authoritativeState.ResolveBinding("session-1")?.ProfileId);
        Assert.Equal("remote-old", authoritativeState.ResolveBinding("session-2")?.RemoteSessionId);
        Assert.Equal("profile-2", authoritativeState.ResolveBinding("session-2")?.ProfileId);
        Assert.NotEmpty(authoritativeState.ResolveContentSlice("session-1")?.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty);
        Assert.True(authoritativeState.ResolveContentSlice("session-1")?.ShowPlanPanel);
        Assert.Equal("remote-shared", workspace.GetRemoteBinding("session-1")?.RemoteSessionId);
        Assert.Equal("profile-1", workspace.GetRemoteBinding("session-1")?.BoundProfileId);
        Assert.Equal("remote-old", workspace.GetRemoteBinding("session-2")?.RemoteSessionId);
        Assert.Equal("profile-2", workspace.GetRemoteBinding("session-2")?.BoundProfileId);
        var session1Snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(session1Snapshot);
        Assert.NotEmpty(session1Snapshot!.Transcript);
        Assert.True(session1Snapshot.ShowPlanPanel);
    }

    private static Mock<IChatStore> CreateChatStore(IState<ChatState> state, ChatState initialState)
    {
        var authoritativeState = initialState;
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(s => s.State).Returns(state);
        chatStore.Setup(s => s.GetCurrentStateAsync())
            .ReturnsAsync(() => authoritativeState);
        chatStore.Setup(s => s.Dispatch(It.IsAny<ChatAction>()))
            .Returns<ChatAction>(async action =>
            {
                authoritativeState = ChatReducer.Reduce(authoritativeState, action);
                await state.Update(_ => authoritativeState, default);
            });
        return chatStore;
    }

    
    [Fact]
    public async Task UpdateBinding_PersistsRemoteOwnershipBeforeRestart()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var preferences = CreatePreferences(syncContext);
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        using (var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext))
        {
            await workspace.RestoreAsync(TestContext.Current.CancellationToken);
            workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "session-1",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc)));
            await workspace.SaveAsync(TestContext.Current.CancellationToken);

            var initialState = ChatState.Empty;
            var state = State.Value(this, () => initialState);
            var chatStore = CreateChatStore(state, initialState);
            var coordinator = new BindingCoordinator(workspace, chatStore.Object);

            var result = await coordinator.UpdateBindingAsync("session-1", "remote-1", "profile-1");
            Assert.Equal(BindingUpdateStatus.Success, result.Status);
        }

        var saved = Assert.IsType<ConversationDocument>(store.LastSavedDocument);
        var record = Assert.Single(saved.Conversations);
        Assert.Equal("remote-1", record.RemoteSessionId);
        Assert.Equal("profile-1", record.BoundProfileId);

        store.LoadResult = saved;
        using var restoredWorkspace = CreateWorkspace(store, new FakeSessionManager(), preferences, syncContext);
        await restoredWorkspace.RestoreAsync(TestContext.Current.CancellationToken);
        var binding = restoredWorkspace.GetRemoteBinding("session-1");
        Assert.NotNull(binding);
        Assert.Equal("remote-1", binding!.RemoteSessionId);
        Assert.Equal("profile-1", binding.BoundProfileId);
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
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            prefsLogger.Object,
            new ImmediateUiDispatcher(),
            TestSystemNotificationService.Instance);
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

        public IEnumerable<Session> GetAllSessions() => _sessions.Values;

        public Session? GetSession(string sessionId)
            => _sessions.TryGetValue(sessionId, out var session) ? session : null;

        public Task<Session> CreateSessionAsync(string sessionId, string cwd)
        {
            var session = new Session(sessionId, cwd)
            {
                DisplayName = sessionId
            };
            _sessions[sessionId] = session;
            return Task.FromResult(session);
        }

        public bool RemoveSession(string sessionId) => _sessions.Remove(sessionId);

        public Task<bool> CancelSessionAsync(string sessionId)
            => Task.FromResult(_sessions.ContainsKey(sessionId));

        public Session GetOrCreateTrackingSlot(string sessionId, string cwd)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                session = new Session(sessionId, cwd)
                {
                    DisplayName = sessionId
                };
                _sessions[sessionId] = session;
            }

            return session;
        }
    }
}
