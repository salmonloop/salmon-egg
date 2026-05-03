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
using SalmonEgg.Domain.Models.Protocol;
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
        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.RestoreAsync());

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 5, 2, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 2, 0, 0, 1, DateTimeKind.Utc),
            AvailableCommands: availableCommands));

        await AwaitWithSynchronizationContextAsync(syncContext, viewModel.ReplaceChatServiceAsync(chatService.Object));
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
            return Task.FromResult(
                syncContext.PendingCount == 0
                && string.Equals(viewModel.CurrentSessionId, "conv-local", StringComparison.Ordinal)
                && string.Equals(viewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal));
        });

        var collectionActions = new List<NotifyCollectionChangedAction>();
        viewModel.AvailableSlashCommands.CollectionChanged += (_, args) => collectionActions.Add(args.Action);

        var switchTask = viewModel.SwitchConversationAsync("conv-remote");
        await AwaitWithSynchronizationContextAsync(syncContext, switchTask);

        Assert.True(await switchTask);

        await WaitForConditionAsync(() =>
        {
            syncContext.RunAll();
            return Task.FromResult(
                syncContext.PendingCount == 0
                && string.Equals(viewModel.CurrentSessionId, "conv-remote", StringComparison.Ordinal)
                && viewModel.AvailableSlashCommands.Count == availableCommands.Count);
        });

        Assert.DoesNotContain(collectionActions, action => action == NotifyCollectionChangedAction.Reset);
        Assert.Equal(availableCommands.Count, collectionActions.Count(action => action == NotifyCollectionChangedAction.Add));
        Assert.Equal(availableCommands.Select(command => command.Name), viewModel.AvailableSlashCommands.Select(command => command.Name));
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
