using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task ConnectionProjection_WhenAvailableCommandsUnchanged_DoesNotResetSlashCommands()
    {
        var syncContext = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;
        var availableCommands = CreateAvailableCommands("plan", "review");

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1")),
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty
                .Add(
                    "conv-1",
                    new ConversationSessionStateSlice(
                        ImmutableList<ConversationModeOptionSnapshot>.Empty,
                        null,
                        ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                        false,
                        availableCommands,
                        null,
                        null))
        });

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                syncContext.PendingCount == 0
                && viewModel.AvailableSlashCommands.Count == availableCommands.Count);
        });

        var initialItems = viewModel.AvailableSlashCommands.ToArray();
        var collectionActions = new List<NotifyCollectionChangedAction>();
        viewModel.AvailableSlashCommands.CollectionChanged += (_, args) => collectionActions.Add(args.Action);

        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-2"));

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                syncContext.PendingCount == 0
                && string.Equals(viewModel.ConnectionInstanceId, "conn-2", StringComparison.Ordinal));
        });

        Assert.Empty(collectionActions);
        Assert.Equal(
            initialItems,
            viewModel.AvailableSlashCommands,
            ReferenceEqualityComparer.Instance);
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenWarmConversationAlreadyProjected_DoesNotRebuildSlashCommands()
    {
        var syncContext = new QueueingSynchronizationContext();
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        var availableCommands = CreateAvailableCommands("plan", "review");

        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;

        fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.RestoreAsync(TestContext.Current.CancellationToken));

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 2, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 2, 0, 0, 1, DateTimeKind.Utc),
            AvailableCommands: availableCommands));

        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1")),
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty
                .Add(
                    "conv-remote",
                    new ConversationSessionStateSlice(
                        ImmutableList<ConversationModeOptionSnapshot>.Empty,
                        null,
                        ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                        false,
                        availableCommands,
                        null,
                        null)),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                .Add(
                    "conv-remote",
                    new ConversationRuntimeSlice(
                        "conv-remote",
                        ConversationRuntimePhase.Warm,
                        "conn-1",
                        "remote-1",
                        "profile-1",
                        "SessionLoadCompleted",
                        new DateTime(2026, 5, 2, 0, 0, 2, DateTimeKind.Utc)))
        });

        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(syncContext.PendingCount == 0);
        });

        var collectionActions = new List<NotifyCollectionChangedAction>();
        viewModel.AvailableSlashCommands.CollectionChanged += (_, args) => collectionActions.Add(args.Action);

        var switchTask = viewModel.SwitchConversationAsync("conv-remote", TestContext.Current.CancellationToken);
        await AwaitWithSynchronizationContextAsync(syncContext, switchTask);

        Assert.True(await switchTask);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(syncContext.PendingCount == 0);
        });

        Assert.DoesNotContain(collectionActions, action => action == NotifyCollectionChangedAction.Reset);
        Assert.True(collectionActions.Count(action => action == NotifyCollectionChangedAction.Add) <= availableCommands.Count);
        chatService.Verify(
            service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }


    [Fact]
    public async Task SwitchConversationAsync_WhenReturningToWarmRemoteConversation_MaterializesTranscriptFromRuntimeProjection()
    {
        // Evidence chain:
        // 1) Remote sessions use SelectionOnly activation and may clear non-authoritative store content.
        // 2) Warm short-circuit then skips session/load when RuntimeProjection/snapshot is reusable.
        // 3) Without rematerializing empty store content from that snapshot, A->B->A projects a blank transcript.
        var syncContext = new QueueingSynchronizationContext();
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.IsAny<SessionLoadParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        await using var fixture = CreateViewModel(syncContext);
        var viewModel = fixture.ViewModel;

        fixture.Profiles.Profiles.Add(CreateConnectableStdioProfile("profile-1", "Profile 1"));
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.RestoreAsync(TestContext.Current.CancellationToken));

        var warmMessage = new ConversationMessageSnapshot
        {
            Id = "warm-a-1",
            Timestamp = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            IsOutgoing = false,
            ContentType = "text",
            TextContent = "warm remote A transcript must return"
        };

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-a",
            Transcript: [warmMessage],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1",
            SessionInfo: new ConversationSessionInfoSnapshot { Title = "Warm A" }),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-b",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "warm-b-1",
                    Timestamp = new DateTime(2026, 5, 2, 0, 0, 1, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "warm remote B transcript"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 2, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 2, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1",
            SessionInfo: new ConversationSessionInfoSnapshot { Title = "Warm B" }),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);

        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object, TestContext.Current.CancellationToken));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-a",
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty
                .Add("conv-a", new ConversationContentSlice(
                    ImmutableList.Create(warmMessage),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false))
                .Add("conv-b", new ConversationContentSlice(
                    ImmutableList.Create(
                        new ConversationMessageSnapshot
                        {
                            Id = "warm-b-1",
                            Timestamp = new DateTime(2026, 5, 2, 0, 0, 1, DateTimeKind.Utc),
                            IsOutgoing = false,
                            ContentType = "text",
                            TextContent = "warm remote B transcript"
                        }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false)),
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty
                .Add(
                    "conv-a",
                    new ConversationSessionStateSlice(
                        ImmutableList<ConversationModeOptionSnapshot>.Empty,
                        null,
                        ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                        false,
                        ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                        new ConversationSessionInfoSnapshot { Title = "Warm A" },
                        null))
                .Add(
                    "conv-b",
                    new ConversationSessionStateSlice(
                        ImmutableList<ConversationModeOptionSnapshot>.Empty,
                        null,
                        ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                        false,
                        ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                        new ConversationSessionInfoSnapshot { Title = "Warm B" },
                        null)),
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-a", new ConversationBindingSlice("conv-a", "remote-a", "profile-1"))
                .Add("conv-b", new ConversationBindingSlice("conv-b", "remote-b", "profile-1")),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                .Add(
                    "conv-a",
                    new ConversationRuntimeSlice(
                        "conv-a",
                        ConversationRuntimePhase.Warm,
                        "conn-1",
                        "remote-a",
                        "profile-1",
                        ConversationRuntimeReasons.SessionLoadCompleted,
                        new DateTime(2026, 5, 2, 0, 0, 2, DateTimeKind.Utc)))
                .Add(
                    "conv-b",
                    new ConversationRuntimeSlice(
                        "conv-b",
                        ConversationRuntimePhase.Warm,
                        "conn-1",
                        "remote-b",
                        "profile-1",
                        ConversationRuntimeReasons.SessionLoadCompleted,
                        new DateTime(2026, 5, 2, 0, 0, 3, DateTimeKind.Utc)))
        });

        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await fixture.ApplyCurrentStoreProjectionAsync();

        await AwaitWithSynchronizationContextAsync(
            syncContext,
            viewModel.SwitchConversationAsync("conv-b", TestContext.Current.CancellationToken));
        Assert.Equal("conv-b", viewModel.CurrentSessionId);

        // Simulate the SelectionOnly non-authoritative clear that can leave store content empty
        // while RuntimeProjection still owns the warm transcript for A.
        await fixture.UpdateStateAsync(state => state with
        {
            ConversationContents = (state.ConversationContents ?? ImmutableDictionary<string, ConversationContentSlice>.Empty)
                .SetItem(
                    "conv-a",
                    new ConversationContentSlice(
                        ImmutableList<ConversationMessageSnapshot>.Empty,
                        ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                        false))
        });

        await AwaitWithSynchronizationContextAsync(
            syncContext,
            viewModel.SwitchConversationAsync("conv-a", TestContext.Current.CancellationToken));
        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                string.Equals(viewModel.CurrentSessionId, "conv-a", StringComparison.Ordinal)
                && viewModel.MessageHistory.Any(message =>
                    string.Equals(message.TextContent, warmMessage.TextContent, StringComparison.Ordinal)));
        }, timeoutMilliseconds: 5000);

        Assert.Contains(
            viewModel.MessageHistory,
            message => string.Equals(message.TextContent, warmMessage.TextContent, StringComparison.Ordinal));
        chatService.Verify(
            service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ImmutableList<ConversationAvailableCommandSnapshot> CreateAvailableCommands(params string[] names)
        => names.Select(name => new ConversationAvailableCommandSnapshot(name, $"{name} command", $"{name}-hint"))
            .ToImmutableList();

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
