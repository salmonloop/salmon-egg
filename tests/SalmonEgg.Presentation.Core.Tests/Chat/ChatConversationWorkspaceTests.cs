using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Models.Navigation;
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
        Assert.Empty(snapshot.AvailableModes ?? Array.Empty<ConversationModeOptionSnapshot>());
        Assert.Null(snapshot.SelectedModeId);
        Assert.Empty(snapshot.ConfigOptions ?? Array.Empty<ConversationConfigOptionSnapshot>());
        Assert.False(snapshot.ShowConfigOptionsPanel);
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
    public async Task RestoreAsync_WithLegacyNullTranscriptEntries_SkipsNullsAndStillSaves()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore
        {
            LoadResult = new ConversationDocument
            {
                Conversations =
                {
                    new ConversationRecord
                    {
                        ConversationId = "session-null",
                        Messages =
                        {
                            CreateTextMessage("m-1", "hello"),
                            null!
                        }
                    }
                }
            }
        };

        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        await workspace.RestoreAsync();
        await workspace.SaveAsync();

        var snapshot = workspace.GetConversationSnapshot("session-null");
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Transcript);
        Assert.Equal("m-1", snapshot.Transcript[0].Id);
    }

    [Fact]
    public async Task SaveAsync_PersistsConversationSessionState()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1", "hello")
            ],
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc),
            AvailableModes:
            [
                new ConversationModeOptionSnapshot
                {
                    ModeId = "plan",
                    ModeName = "Plan",
                    Description = "Planning mode"
                }
            ],
            SelectedModeId: "plan",
            ConfigOptions:
            [
                new ConversationConfigOptionSnapshot
                {
                    Id = "mode",
                    Name = "Mode",
                    Category = "mode",
                    ValueType = "select",
                    SelectedValue = "plan",
                    Options =
                    [
                        new ConversationConfigOptionChoiceSnapshot
                        {
                            Value = "plan",
                            Name = "Plan",
                            Description = "Planning mode"
                        }
                    ]
                }
            ],
            ShowConfigOptionsPanel: true));

        await workspace.SaveAsync();

        var saved = Assert.IsType<ConversationDocument>(store.LastSavedDocument);
        var conversation = Assert.Single(saved.Conversations);
        Assert.Equal("plan", conversation.SelectedModeId);
        Assert.True(conversation.ShowConfigOptionsPanel);
        var availableMode = Assert.Single(conversation.AvailableModes);
        Assert.Equal("plan", availableMode.ModeId);
        var configOption = Assert.Single(conversation.ConfigOptions);
        Assert.Equal("mode", configOption.Id);
        Assert.Equal("plan", configOption.SelectedValue);
    }

    [Fact]
    public async Task SaveAsync_ProtocolMessageId_RoundTripsThroughConversationDocument()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);

        using (var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext))
        {
            var message = CreateTextMessage("m-1", "hello");
            message.ProtocolMessageId = "protocol-1";

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
                LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc)));

            await workspace.SaveAsync();
        }

        var saved = Assert.IsType<ConversationDocument>(store.LastSavedDocument);
        var savedConversation = Assert.Single(saved.Conversations);
        var savedMessage = Assert.Single(savedConversation.Messages);
        Assert.Equal("protocol-1", savedMessage.ProtocolMessageId);

        store.LoadResult = saved;
        using var restoredWorkspace = CreateWorkspace(store, new FakeSessionManager(), preferences, syncContext);
        await restoredWorkspace.RestoreAsync();

        var restored = restoredWorkspace.GetConversationSnapshot("session-1");
        Assert.NotNull(restored);
        var restoredMessage = Assert.Single(restored!.Transcript);
        Assert.Equal("protocol-1", restoredMessage.ProtocolMessageId);
    }

    [Fact]
    public async Task UpsertConversationSnapshot_PreservesStructuredToolCallContentInSnapshotsAndPersistence()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "tool-1",
                    ContentType = "tool_call",
                    ToolCallId = "call-1",
                    ToolCallStatus = ToolCallStatus.InProgress,
                    ToolCallContent = new List<ToolCallContent>
                    {
                        new ContentToolCallContent(new ResourceLinkContentBlock("https://example.com/doc"))
                    },
                    Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc)));

        var snapshot = workspace.GetConversationSnapshot("session-1");
        var toolCall = Assert.Single(snapshot!.Transcript);
        var structuredContent = Assert.Single(toolCall.ToolCallContent!);
        var content = Assert.IsType<ContentToolCallContent>(structuredContent);
        var resourceLink = Assert.IsType<ResourceLinkContentBlock>(content.Content);
        Assert.Equal("https://example.com/doc", resourceLink.Uri);

        await workspace.SaveAsync();

        var saved = Assert.IsType<ConversationDocument>(store.LastSavedDocument);
        var conversation = Assert.Single(saved.Conversations);
        var savedToolCall = Assert.Single(conversation.Messages);
        var savedStructuredContent = Assert.Single(savedToolCall.ToolCallContent!);
        var savedContent = Assert.IsType<ContentToolCallContent>(savedStructuredContent);
        var savedResourceLink = Assert.IsType<ResourceLinkContentBlock>(savedContent.Content);
        Assert.Equal("https://example.com/doc", savedResourceLink.Uri);
    }

    [Fact]
    public async Task GetConversationSnapshot_WhenTranscriptMutatesConcurrently_DoesNotThrow()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            CreateTranscript("seed", 1024),
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc)));

        var started = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        Exception? failure = null;

        var snapshotReader = Task.Run(() =>
        {
            started.Wait(cts.Token);
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    _ = workspace.GetConversationSnapshot("session-1");
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                failure = ex;
                cts.Cancel();
            }
        });

        var transcriptMutator = Task.Run(() =>
        {
            started.Wait(cts.Token);
            try
            {
                var counter = 0;
                while (!cts.IsCancellationRequested)
                {
                    workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                        ConversationId: "session-1",
                        Transcript: CreateTranscript($"mutated-{counter}", 1024),
                        Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
                        ShowPlanPanel: false,
                        PlanTitle: null,
                        CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc).AddSeconds(counter)));
                    counter++;
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                failure = ex;
                cts.Cancel();
            }
        });

        started.Set();
        await Task.Delay(200, CancellationToken.None);
        cts.Cancel();
        await Task.WhenAll(snapshotReader, transcriptMutator);

        Assert.Null(failure);
    }

    [Fact]
    public async Task RestoreAsync_RestoresConversationSessionState()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore
        {
            LoadResult = new ConversationDocument
            {
                Conversations =
                {
                    new ConversationRecord
                    {
                        ConversationId = "session-1",
                        DisplayName = "Session One",
                        CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedAt = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                        AvailableModes =
                        {
                            new ConversationModeOptionSnapshot
                            {
                                ModeId = "agent",
                                ModeName = "Agent",
                                Description = "Agent mode"
                            },
                            new ConversationModeOptionSnapshot
                            {
                                ModeId = "plan",
                                ModeName = "Plan",
                                Description = "Plan mode"
                            }
                        },
                        SelectedModeId = "plan",
                        ConfigOptions =
                        {
                            new ConversationConfigOptionSnapshot
                            {
                                Id = "mode",
                                Name = "Mode",
                                Category = "mode",
                                ValueType = "select",
                                SelectedValue = "plan",
                                Options =
                                {
                                    new ConversationConfigOptionChoiceSnapshot
                                    {
                                        Value = "agent",
                                        Name = "Agent"
                                    },
                                    new ConversationConfigOptionChoiceSnapshot
                                    {
                                        Value = "plan",
                                        Name = "Plan"
                                    }
                                }
                            }
                        },
                        ShowConfigOptionsPanel = true
                    }
                }
            }
        };

        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        await workspace.RestoreAsync();

        var snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(snapshot);
        Assert.Equal("plan", snapshot!.SelectedModeId);
        Assert.True(snapshot.ShowConfigOptionsPanel);
        Assert.Equal(2, snapshot.AvailableModes?.Count);
        Assert.Single(snapshot.ConfigOptions ?? Array.Empty<ConversationConfigOptionSnapshot>());
        Assert.Equal("plan", snapshot.ConfigOptions![0].SelectedValue);
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
    public async Task TrySwitchToSessionAsync_UpdatesLastAccessedAt_WithoutChangingLastUpdatedOrder()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-old",
            Transcript:
            [
                CreateTextMessage("m-old", "older")
            ],
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
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
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 2, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 3, 0, DateTimeKind.Utc)));

        await workspace.SaveAsync();
        var beforeAccessedAt = Assert.IsType<ConversationDocument>(store.LastSavedDocument)
            .Conversations
            .Single(c => c.ConversationId == "session-old")
            .LastAccessedAt;

        await workspace.TrySwitchToSessionAsync("session-old");
        await workspace.SaveAsync();

        Assert.Equal(new[] { "session-new", "session-old" }, workspace.GetKnownConversationIds());
        var updatedRecord = Assert.IsType<ConversationDocument>(store.LastSavedDocument)
            .Conversations
            .Single(c => c.ConversationId == "session-old");
        Assert.True(updatedRecord.LastAccessedAt > beforeAccessedAt);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc), updatedRecord.LastUpdatedAt);
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
    public async Task SaveAsync_RoundTripsProjectAffinityOverride()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        await workspace.RegisterConversationAsync("session-1");
        workspace.UpdateProjectAffinityOverride("session-1", "project-override");

        await workspace.SaveAsync();

        var saved = Assert.IsType<ConversationDocument>(store.LastSavedDocument);
        var record = Assert.Single(saved.Conversations);
        Assert.Equal("project-override", record.ProjectAffinityOverrideProjectId);

        var restoreStore = new CapturingConversationStore
        {
            LoadResult = saved
        };
        var restoreSessionManager = new FakeSessionManager();
        var restorePreferences = CreatePreferences(syncContext);
        using var restoredWorkspace = CreateWorkspace(restoreStore, restoreSessionManager, restorePreferences, syncContext);
        await restoredWorkspace.RestoreAsync();

        var overrideValue = restoredWorkspace.GetProjectAffinityOverride("session-1");
        Assert.NotNull(overrideValue);
        Assert.Equal("project-override", overrideValue!.ProjectId);
    }

    [Fact]
    public async Task UpdateProjectAffinityOverride_UpdatesCatalogStateWithoutChangingRemoteBinding()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");
        await workspace.RegisterConversationAsync("session-1");
        workspace.UpdateRemoteBinding("session-1", "remote-1", "profile-1");

        var beforeVersion = workspace.ConversationListVersion;
        var beforeRemoteBinding = workspace.GetRemoteBinding("session-1");

        workspace.UpdateProjectAffinityOverride("session-1", "project-override");

        Assert.Equal(beforeVersion + 1, workspace.ConversationListVersion);
        var afterRemoteBinding = workspace.GetRemoteBinding("session-1");
        Assert.NotNull(afterRemoteBinding);
        Assert.Equal(beforeRemoteBinding?.RemoteSessionId, afterRemoteBinding!.RemoteSessionId);
        Assert.Equal(beforeRemoteBinding?.BoundProfileId, afterRemoteBinding.BoundProfileId);
        var overrideValue = workspace.GetProjectAffinityOverride("session-1");
        Assert.NotNull(overrideValue);
        Assert.Equal("project-override", overrideValue!.ProjectId);
    }

    [Fact]
    public async Task MoveConversationToProject_AllowsExplicitUnclassifiedOverride()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        await workspace.RegisterConversationAsync("session-1");

        workspace.MoveConversationToProject("session-1", NavigationProjectIds.Unclassified);

        var overrideValue = workspace.GetProjectAffinityOverride("session-1");
        Assert.NotNull(overrideValue);
        Assert.Equal(NavigationProjectIds.Unclassified, overrideValue!.ProjectId);
    }

    [Fact]
    public async Task GetCatalog_IncludesRemoteBindingAndProjectAffinityOverrideMetadata()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        await sessionManager.CreateSessionAsync("session-1", "/remote/repo/feature");

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
        workspace.UpdateRemoteBinding("session-1", "remote-1", "profile-1");
        workspace.UpdateProjectAffinityOverride("session-1", "project-override");

        var catalog = Assert.Single(workspace.GetCatalog());

        Assert.Equal("remote-1", catalog.RemoteSessionId);
        Assert.Equal("profile-1", catalog.BoundProfileId);
        Assert.Equal("project-override", catalog.ProjectAffinityOverrideProjectId);
    }

    [Fact]
    public async Task SaveAsync_PersistsLastAccessedAt_SeparatelyFromLastUpdatedAt()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1", "hello")
            ],
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc)));

        await workspace.TrySwitchToSessionAsync("session-1");
        await workspace.SaveAsync();

        var saved = Assert.IsType<ConversationDocument>(store.LastSavedDocument);
        var conversation = Assert.Single(saved.Conversations);
        Assert.True(conversation.LastAccessedAt >= conversation.LastUpdatedAt);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc), conversation.LastUpdatedAt);
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
    public async Task RestoreAsync_WithoutLastActiveConversationId_UsesMostRecentlyAccessedConversation()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore
        {
            LoadResult = new ConversationDocument
            {
                Conversations =
                {
                    new ConversationRecord
                    {
                        ConversationId = "session-updated",
                        DisplayName = "Updated",
                        CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedAt = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                        LastAccessedAt = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    },
                    new ConversationRecord
                    {
                        ConversationId = "session-accessed",
                        DisplayName = "Accessed",
                        CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastUpdatedAt = new DateTime(2026, 3, 1, 0, 30, 0, DateTimeKind.Utc),
                        LastAccessedAt = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                    }
                }
            }
        };

        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        await workspace.RestoreAsync();

        Assert.Equal("session-accessed", workspace.LastActiveConversationId);
        Assert.Equal(new[] { "session-updated", "session-accessed" }, workspace.GetKnownConversationIds());
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

    [Fact]
    public async Task UpsertConversationSnapshot_ExistingConversation_ReordersCatalog()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1", "alpha")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-2",
            Transcript:
            [
                CreateTextMessage("m-2", "beta")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc)));

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1-2", "alpha-updated")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 5, 0, DateTimeKind.Utc)));

        var knownIds = workspace.GetKnownConversationIds();
        Assert.Equal(new[] { "session-1", "session-2" }, knownIds);
        Assert.Equal(3, workspace.ConversationListVersion);
    }

    [Fact]
    public void UpdateRemoteBinding_ExistingConversation_DoesNotReorderCatalogOrAdvanceListVersion()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-old",
            Transcript:
            [
                CreateTextMessage("m-old", "older")
            ],
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
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
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 2, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 3, 0, DateTimeKind.Utc)));

        var versionBeforeBindingUpdate = workspace.ConversationListVersion;
        var oldCatalogBefore = workspace.GetCatalog().Single(item => item.ConversationId == "session-old");

        workspace.UpdateRemoteBinding("session-old", "remote-new", "profile-new");

        var knownIds = workspace.GetKnownConversationIds();
        var oldCatalogAfter = workspace.GetCatalog().Single(item => item.ConversationId == "session-old");
        var remoteBinding = workspace.GetRemoteBinding("session-old");

        Assert.Equal(new[] { "session-new", "session-old" }, knownIds);
        Assert.Equal(versionBeforeBindingUpdate, workspace.ConversationListVersion);
        Assert.Equal(oldCatalogBefore.LastUpdatedAt, oldCatalogAfter.LastUpdatedAt);
        Assert.NotNull(remoteBinding);
        Assert.Equal("remote-new", remoteBinding!.RemoteSessionId);
        Assert.Equal("profile-new", remoteBinding.BoundProfileId);
    }

    [Fact]
    public async Task ApplySessionInfoUpdateAsync_UnknownConversation_DoesNotRecreateConversation()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1", "alpha")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));

        workspace.DeleteConversation("session-1");
        Assert.DoesNotContain("session-1", workspace.GetKnownConversationIds());

        await workspace.ApplySessionInfoUpdateAsync(
            "session-1",
            title: "zombie",
            updatedAtUtc: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.DoesNotContain("session-1", workspace.GetKnownConversationIds());
        Assert.Null(workspace.GetConversationSnapshot("session-1"));
        Assert.Null(sessionManager.GetSession("session-1"));
    }

    [Fact]
    public async Task ApplySessionInfoUpdateAsync_DeletedConversation_WithAllowRegister_DoesNotResurrectConversation()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1", "alpha")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));

        workspace.DeleteConversation("session-1");
        Assert.DoesNotContain("session-1", workspace.GetKnownConversationIds());

        await workspace.ApplySessionInfoUpdateAsync(
            "session-1",
            title: "zombie",
            updatedAtUtc: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            allowRegisterWhenMissing: true);

        Assert.DoesNotContain("session-1", workspace.GetKnownConversationIds());
        Assert.Null(workspace.GetConversationSnapshot("session-1"));
        Assert.Null(sessionManager.GetSession("session-1"));
    }

    [Fact]
    public async Task ApplySessionInfoSnapshotAsync_PartialUpdate_MergesMetadataWithoutDroppingMeta()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Original title",
                Description = "Original description",
                Cwd = @"C:\repo\one",
                UpdatedAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["existing"] = "value",
                    ["shared"] = "before"
                }
            }));

        await workspace.ApplySessionInfoSnapshotAsync(
            "session-1",
            new ConversationSessionInfoSnapshot
            {
                Description = "Updated description",
                UpdatedAtUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["shared"] = "after",
                    ["added"] = 2
                }
            });

        var snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(snapshot);
        var sessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(snapshot!.SessionInfo);
        Assert.Equal("Original title", sessionInfo.Title);
        Assert.Equal("Updated description", sessionInfo.Description);
        Assert.Equal(@"C:\repo\one", sessionInfo.Cwd);
        Assert.Equal(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), sessionInfo.UpdatedAtUtc);
        Assert.Equal("value", sessionInfo.Meta!["existing"]);
        Assert.Equal("after", sessionInfo.Meta["shared"]);
        Assert.Equal(2, sessionInfo.Meta["added"]);
    }

    [Fact]
    public async Task ApplySessionInfoSnapshotAsync_EmptyOrWhitespaceStrings_PreserveExistingSessionInfoFields()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Original title",
                Description = "Original description",
                Cwd = @"C:\repo\one"
            }));

        await workspace.ApplySessionInfoSnapshotAsync(
            "session-1",
            new ConversationSessionInfoSnapshot
            {
                Title = string.Empty,
                Description = "   ",
                Cwd = "\t",
                UpdatedAtUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)
            });

        var snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(snapshot);
        var sessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(snapshot!.SessionInfo);
        Assert.Equal("Original title", sessionInfo.Title);
        Assert.Equal("Original description", sessionInfo.Description);
        Assert.Equal(@"C:\repo\one", sessionInfo.Cwd);
        Assert.Equal(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), sessionInfo.UpdatedAtUtc);
    }

    [Fact]
    public async Task ApplySessionInfoSnapshotAsync_WhitespaceFields_StillMergeMetadata()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Original title",
                Description = "Original description",
                Cwd = @"C:\repo\one",
                UpdatedAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["existing"] = "value",
                    ["shared"] = "before"
                }
            }));

        await workspace.ApplySessionInfoSnapshotAsync(
            "session-1",
            new ConversationSessionInfoSnapshot
            {
                Title = " ",
                Description = "\t",
                Cwd = " ",
                UpdatedAtUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["shared"] = "after",
                    ["added"] = 2
                }
            });

        var snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(snapshot);
        var sessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(snapshot!.SessionInfo);
        Assert.Equal("Original title", sessionInfo.Title);
        Assert.Equal("Original description", sessionInfo.Description);
        Assert.Equal(@"C:\repo\one", sessionInfo.Cwd);
        Assert.Equal(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), sessionInfo.UpdatedAtUtc);
        Assert.Equal("value", sessionInfo.Meta!["existing"]);
        Assert.Equal("after", sessionInfo.Meta["shared"]);
        Assert.Equal(2, sessionInfo.Meta["added"]);
    }

    [Fact]
    public async Task ApplySessionInfoSnapshotAsync_WhitespaceFields_PreserveExistingValues()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Original title",
                Description = "Original description",
                Cwd = @"C:\repo\one",
                UpdatedAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            }));

        await workspace.ApplySessionInfoSnapshotAsync(
            "session-1",
            new ConversationSessionInfoSnapshot
            {
                Title = string.Empty,
                Description = "   ",
                Cwd = "\t",
                UpdatedAtUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)
            });

        var sessionInfo = workspace.GetConversationSnapshot("session-1")!.SessionInfo;
        Assert.NotNull(sessionInfo);
        Assert.Equal("Original title", sessionInfo!.Title);
        Assert.Equal("Original description", sessionInfo.Description);
        Assert.Equal(@"C:\repo\one", sessionInfo.Cwd);
        Assert.Equal(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), sessionInfo.UpdatedAtUtc);
    }

    [Fact]
    public async Task ApplySessionInfoSnapshotAsync_RemoteMetadataRefreshDoesNotOverrideEstablishedSessionCwd()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);

        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");

        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Original title"
            }));

        await workspace.ApplySessionInfoSnapshotAsync(
            "session-1",
            new ConversationSessionInfoSnapshot
            {
                Title = "Refreshed title",
                Cwd = @"C:\Users\shang\AppData\Local\SalmonEgg",
                UpdatedAtUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)
            });

        var snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(snapshot);
        var sessionInfo = Assert.IsType<ConversationSessionInfoSnapshot>(snapshot!.SessionInfo);
        Assert.Equal("Refreshed title", sessionInfo.Title);
        Assert.Equal(@"C:\repo\one", sessionInfo.Cwd);
        Assert.Equal(
            @"C:\repo\one",
            sessionManager.GetSession("session-1")!.Cwd);
    }

    [Fact]
    public async Task GetCatalog_WhenSessionManagerCwdDrifts_UsesWorkspaceSessionInfoCwdForProjection()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);

        var session = await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");

        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Original title",
                Cwd = @"C:\repo\one"
            }));

        session.Cwd = @"C:\Users\shang\AppData\Local\SalmonEgg";

        var item = Assert.Single(workspace.GetCatalog());
        Assert.Equal(@"C:\repo\one", item.Cwd);
    }

    [Fact]
    public async Task UpsertConversationSnapshot_SeedsSessionInfoCwdFromEstablishedConversationContext()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);

        await sessionManager.CreateSessionAsync("session-1", @"C:\repo\one");

        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Original title"
            }));

        var sessionInfo = workspace.GetConversationSnapshot("session-1")!.SessionInfo;
        Assert.NotNull(sessionInfo);
        Assert.Equal(@"C:\repo\one", sessionInfo!.Cwd);
    }

    [Fact]
    public void UpsertConversationSnapshot_WhenIncomingSnapshotOmitsSessionInfo_PreservesExistingSessionInfoAuthority()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);

        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Original title",
                Cwd = @"C:\repo\one"
            }));

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript:
            [
                CreateTextMessage("m-1", "fresh transcript")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: null));

        var snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Transcript);
        Assert.NotNull(snapshot.SessionInfo);
        Assert.Equal("Original title", snapshot.SessionInfo!.Title);
        Assert.Equal(@"C:\repo\one", snapshot.SessionInfo.Cwd);

        var catalogItem = Assert.Single(workspace.GetCatalog());
        Assert.Equal(@"C:\repo\one", catalogItem.Cwd);
    }

    [Fact]
    public async Task GetCatalog_WhenLocalConversationSessionIsMissing_UsesRemoteSessionSetupCwd()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);

        await sessionManager.CreateSessionAsync("remote-1", @"C:\repo\one");

        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-1", "profile-1");

        var item = Assert.Single(workspace.GetCatalog());
        Assert.Equal(@"C:\repo\one", item.Cwd);
    }

    [Fact]
    public async Task ApplySessionInfoSnapshotAsync_WhenOnlyRemoteSessionCarriesEstablishedCwd_PreservesThatCwd()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);

        await sessionManager.CreateSessionAsync("remote-1", @"C:\repo\one");

        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-1", "profile-1");

        await workspace.ApplySessionInfoSnapshotAsync(
            "session-1",
            new ConversationSessionInfoSnapshot
            {
                Title = "Refreshed title",
                UpdatedAtUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)
            });

        var snapshot = workspace.GetConversationSnapshot("session-1");
        Assert.NotNull(snapshot);
        Assert.Equal(@"C:\repo\one", snapshot!.SessionInfo!.Cwd);
    }

    [Fact]
    public async Task UpsertConversationSnapshot_WhenOnlyRemoteSessionCarriesEstablishedCwd_SeedsSessionInfoFromRemoteBinding()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);

        await sessionManager.CreateSessionAsync("remote-1", @"C:\repo\one");

        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);
        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        workspace.UpdateRemoteBinding("session-1", "remote-1", "profile-1");

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = "Updated title"
            }));

        var sessionInfo = workspace.GetConversationSnapshot("session-1")!.SessionInfo;
        Assert.NotNull(sessionInfo);
        Assert.Equal(@"C:\repo\one", sessionInfo!.Cwd);
    }

    [Fact]
    public async Task SaveAsync_SessionScopedCommandsAndUsage_RoundTrips()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);

        using (var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext))
        {
            workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "session-1",
                Transcript: [],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                AvailableCommands:
                [
                    new ConversationAvailableCommandSnapshot("plan", "Planning command", "target")
                ],
                Usage: new ConversationUsageSnapshot(
                    3,
                    99,
                    new ConversationUsageCostSnapshot(1.25m, "USD"))));

            await workspace.SaveAsync();
        }

        var saved = store.LastSavedDocument;
        Assert.NotNull(saved);
        var savedConversation = Assert.Single(saved!.Conversations);
        var savedCommand = Assert.Single(savedConversation.AvailableCommands);
        Assert.Equal("plan", savedCommand.Name);
        Assert.Equal("Planning command", savedCommand.Description);
        Assert.Equal("target", savedCommand.InputHint);
        Assert.NotNull(savedConversation.Usage);
        Assert.Equal(3, savedConversation.Usage!.Used);
        Assert.Equal(99, savedConversation.Usage.Size);
        Assert.NotNull(savedConversation.Usage.Cost);
        Assert.Equal(1.25m, savedConversation.Usage.Cost!.Amount);
        Assert.Equal("USD", savedConversation.Usage.Cost.Currency);

        store.LoadResult = saved;
        using var restoredWorkspace = CreateWorkspace(store, new FakeSessionManager(), preferences, syncContext);
        await restoredWorkspace.RestoreAsync();

        var restored = restoredWorkspace.GetConversationSnapshot("session-1");
        Assert.NotNull(restored);
        var restoredCommand = Assert.Single(restored!.AvailableCommands ?? Array.Empty<ConversationAvailableCommandSnapshot>());
        Assert.Equal("plan", restoredCommand.Name);
        Assert.Equal("Planning command", restoredCommand.Description);
        Assert.Equal("target", restoredCommand.InputHint);
        Assert.NotNull(restored.Usage);
        Assert.Equal(3, restored.Usage!.Used);
        Assert.Equal(99, restored.Usage.Size);
        Assert.NotNull(restored.Usage.Cost);
        Assert.Equal(1.25m, restored.Usage.Cost!.Amount);
        Assert.Equal("USD", restored.Usage.Cost.Currency);
    }

    [Fact]
    public void UpsertConversationSnapshot_DeletedConversation_DoesNotResurrectConversation()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [CreateTextMessage("m-1", "alpha")],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        workspace.DeleteConversation("session-1");

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [CreateTextMessage("m-2", "late")],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc)));

        Assert.DoesNotContain("session-1", workspace.GetKnownConversationIds());
        Assert.Null(workspace.GetConversationSnapshot("session-1"));
    }

    [Fact]
    public void UpdateRemoteBinding_DeletedConversation_DoesNotResurrectConversation()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-1",
            Transcript: [CreateTextMessage("m-1", "alpha")],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        workspace.DeleteConversation("session-1");

        workspace.UpdateRemoteBinding("session-1", remoteSessionId: "remote-zombie", boundProfileId: "profile-zombie");

        Assert.DoesNotContain("session-1", workspace.GetKnownConversationIds());
        Assert.Null(workspace.GetRemoteBinding("session-1"));
    }

    [Fact]
    public async Task DeletedConversation_TombstonePersistsAcrossSaveAndRestore()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);

        using (var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext))
        {
            workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "session-1",
                Transcript: [CreateTextMessage("m-1", "alpha")],
                Plan: [],
                ShowPlanPanel: false,
                PlanTitle: null,
                CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
            workspace.DeleteConversation("session-1");
            await workspace.SaveAsync();
        }

        Assert.NotNull(store.LastSavedDocument);
        store.LoadResult = store.LastSavedDocument!;

        using var restoredWorkspace = CreateWorkspace(store, new FakeSessionManager(), preferences, syncContext);
        await restoredWorkspace.RestoreAsync();
        await restoredWorkspace.ApplySessionInfoUpdateAsync(
            "session-1",
            title: "zombie",
            updatedAtUtc: DateTime.UtcNow,
            allowRegisterWhenMissing: true);

        Assert.DoesNotContain("session-1", restoredWorkspace.GetKnownConversationIds());
        Assert.Null(restoredWorkspace.GetConversationSnapshot("session-1"));
    }

    [Fact]
    public async Task ConversationCatalogFacade_DeleteConversationAsync_WaitsForMutationCompletion()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        var tcs = new TaskCompletionSource<ConversationMutationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(a => a.DeleteConversationAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var selection = new Mock<IShellSelectionReadModel>();
        selection.SetupGet(s => s.CurrentSelection)
            .Returns(new NavigationSelectionState.Session("session-1"));

        var facade = new ConversationCatalogFacade(
            workspace,
            new NavigationProjectPreferencesAdapter(preferences),
            activationCoordinator.Object,
            selection.Object,
            Mock.Of<INavigationCoordinator>(),
            Mock.Of<ILogger<ConversationCatalogFacade>>());

        var deleteTask = facade.DeleteConversationAsync("session-1");
        var completed = await Task.WhenAny(deleteTask, Task.Delay(100));
        Assert.True(completed != deleteTask, "DeleteConversationAsync must remain pending until backend mutation completes.");

        tcs.SetResult(new ConversationMutationResult(true, false, null));
        var result = await deleteTask;
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ConversationCatalogFacade_ArchiveCurrentConversation_NavigatesToStartAfterMutationCompletes()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();
        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        var mutationCompleted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(a => a.ArchiveConversationAsync("session-1", "session-1", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                mutationCompleted.TrySetResult(null);
                await Task.Delay(50);
                return new ConversationMutationResult(true, true, null);
            });

        var selection = new Mock<IShellSelectionReadModel>();
        selection.SetupGet(s => s.CurrentSelection)
            .Returns(new NavigationSelectionState.Session("session-1"));

        var navigation = new Mock<INavigationCoordinator>();
        navigation.Setup(n => n.ActivateStartAsync()).Returns(Task.FromResult(true));

        var facade = new ConversationCatalogFacade(
            workspace,
            new NavigationProjectPreferencesAdapter(preferences),
            activationCoordinator.Object,
            selection.Object,
            navigation.Object,
            Mock.Of<ILogger<ConversationCatalogFacade>>());

        var mutationTask = facade.ArchiveConversationAsync("session-1");

        await mutationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await mutationTask;

        navigation.Verify(n => n.ActivateStartAsync(), Times.Once);
    }

    [Fact]
    public async Task UpsertConversationSnapshot_NewConversation_BumpsConversationListVersion()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        var beforeVersion = workspace.ConversationListVersion;

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-new",
            Transcript:
            [
                CreateTextMessage("m-new", "hello")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 5, 0, DateTimeKind.Utc)));

        Assert.Equal(beforeVersion + 1, workspace.ConversationListVersion);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-new",
            Transcript:
            [
                CreateTextMessage("m-new-2", "again")
            ],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 10, 0, DateTimeKind.Utc)));

        Assert.Equal(beforeVersion + 2, workspace.ConversationListVersion);
    }

    [Fact]
    public void UpsertConversationSnapshot_ExistingConversation_ReordersCatalogAndBumpsVersion()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var store = new CapturingConversationStore();
        var sessionManager = new FakeSessionManager();

        var preferences = CreatePreferences(syncContext);
        using var workspace = CreateWorkspace(store, sessionManager, preferences, syncContext);

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-old",
            Transcript:
            [
                CreateTextMessage("m-old", "older")
            ],
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
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
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 2, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 3, 0, DateTimeKind.Utc)));

        Assert.Equal(new[] { "session-new", "session-old" }, workspace.GetKnownConversationIds());
        var versionAfterInitialUpserts = workspace.ConversationListVersion;

        workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "session-old",
            Transcript:
            [
                CreateTextMessage("m-old-2", "newer now")
            ],
            Plan: Array.Empty<ConversationPlanEntrySnapshot>(),
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 3, 1, 0, 4, 0, DateTimeKind.Utc)));

        Assert.Equal(versionAfterInitialUpserts + 1, workspace.ConversationListVersion);
        Assert.Equal(new[] { "session-old", "session-new" }, workspace.GetKnownConversationIds());
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
            return new AppPreferencesViewModel(
                appSettingsService.Object,
                startupService.Object,
                languageService.Object,
                capabilities.Object,
                uiRuntime.Object,
                Mock.Of<ILogger<AppPreferencesViewModel>>(),
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
            ContentType = "text",
            TextContent = text,
            Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static ConversationMessageSnapshot[] CreateTranscript(string prefix, int count)
        => Enumerable.Range(0, count)
            .Select(index => CreateTextMessage($"{prefix}-{index}", $"message-{index}"))
            .ToArray();

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
