using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Collections.Immutable;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class ConversationCatalogDisplayPresenterTests
{
    [Fact]
    public async Task CatalogAndUnreadAttention_AreCombinedWithoutMutatingCatalogFacts()
    {
        // Arrange
        var catalog = new ConversationCatalogPresenter();
        await using var attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
        var attentionStore = new ConversationAttentionStore(attentionState);
        using var presenter = new ConversationCatalogDisplayPresenter(catalog, attentionStore, new ImmediateUiDispatcher());

        var first = new ConversationCatalogItem(
            "conv-1",
            "First session",
            @"C:\repo\first",
            new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 11, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc));
        var second = new ConversationCatalogItem(
            "conv-2",
            "Second session",
            @"C:\repo\second",
            new DateTime(2026, 4, 20, 13, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 15, 0, 0, DateTimeKind.Utc));

        catalog.Refresh([first, second]);

        Assert.Collection(
            presenter.Snapshot,
            item =>
            {
                Assert.Equal("conv-1", item.ConversationId);
                Assert.False(item.HasUnreadAttention);
                Assert.Equal("First session", item.DisplayName);
            },
            item =>
            {
                Assert.Equal("conv-2", item.ConversationId);
                Assert.False(item.HasUnreadAttention);
                Assert.Equal("Second session", item.DisplayName);
            });

        var versionAfterCatalogRefresh = presenter.ConversationListVersion;

        // Act
        await attentionStore.Dispatch(new MarkConversationUnreadAction(
            "conv-2",
            ConversationAttentionSource.AgentMessage,
            new DateTime(2026, 4, 20, 16, 0, 0, DateTimeKind.Utc)));

        await WaitForConditionAsync(() => IsUnread(presenter, "conv-2"));

        var readVersion = (await attentionStore.GetCurrentStateAsync()).Conversations["conv-2"].UnreadVersion;
        await attentionStore.Dispatch(new ClearConversationUnreadAction("conv-2", readVersion));

        await WaitForConditionAsync(() => !IsUnread(presenter, "conv-2"));

        // Assert
        Assert.Equal(versionAfterCatalogRefresh, presenter.ConversationListVersion);
        Assert.Equal([first, second], catalog.Snapshot);

        Assert.Collection(
            presenter.Snapshot,
            item =>
            {
                Assert.Equal("conv-1", item.ConversationId);
                Assert.False(item.HasUnreadAttention);
                Assert.Equal("First session", item.DisplayName);
            },
            item =>
            {
                Assert.Equal("conv-2", item.ConversationId);
                Assert.False(item.HasUnreadAttention);
                Assert.Equal("Second session", item.DisplayName);
            });
    }

    [Fact]
    public async Task LiveOwners_BackgroundTurnAndPendingRequests_UpdateOnlyTheirConversation()
    {
        // Arrange
        var dispatcher = new PumpDispatcher();
        var catalog = new ConversationCatalogPresenter();
        await using var attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
        await using var chatState = State.Value(new object(), () => ChatState.Empty);
        var attention = new ConversationAttentionStore(attentionState);
        var chat = new ChatStore(chatState);
        var panels = new ChatConversationPanelStateCoordinator();
        using var presenter = new ConversationCatalogDisplayPresenter(catalog, attention, dispatcher, chat, panels);
        catalog.Refresh([CatalogItem("foreground"), CatalogItem("background")]);
        dispatcher.Drain();
        var before = catalog.Snapshot.ToArray();

        // Act
        await chat.Dispatch(new BeginTurnAction("background", "turn-b", ChatTurnPhase.Thinking));
        await dispatcher.UntilAsync(() => Status(presenter, "background") == ConversationStatusIcon.Working);
        await attention.Dispatch(new MarkConversationUnreadAction("background", ConversationAttentionSource.AgentMessage, DateTime.UtcNow));
        await dispatcher.UntilAsync(() => IsUnread(presenter, "background"));
        var question = new AskUserRequestViewModel("question", "remote", "Choose one", []);
        panels.StoreAskUserRequest("background", question);
        dispatcher.Drain();

        // Assert
        Assert.Equal(ConversationStatusIcon.Input, Status(presenter, "background"));
        Assert.Equal(ConversationStatusGroup.Other, presenter.Snapshot.Single(item => item.ConversationId == "foreground").StatusGroup);

        // Act: mutate the original request instance held by the pending owner.
        question.ErrorMessage = "Answer delivery failed";
        dispatcher.Drain();

        // Assert
        Assert.Equal(ConversationStatusIcon.Error, Status(presenter, "background"));

        // Act
        question.ErrorMessage = string.Empty;
        var permission = new PermissionRequestViewModel();
        panels.StorePermissionRequest("background", permission);
        dispatcher.Drain();

        // Assert
        Assert.Equal(ConversationStatusIcon.Permission, Status(presenter, "background"));

        // Act
        panels.RemovePermissionRequest("background", permission);
        panels.RemoveAskUserRequest("background", question);
        dispatcher.Drain();
        await chat.Dispatch(new CompleteTurnAction("background", "turn-b", "end_turn", HasStopReason: true));
        await dispatcher.UntilAsync(() => Status(presenter, "background") == ConversationStatusIcon.Unread);
        var readVersion = (await attention.GetCurrentStateAsync()).Conversations["background"].UnreadVersion;
        await attention.Dispatch(new ClearConversationUnreadAction("background", readVersion));
        await dispatcher.UntilAsync(() => Status(presenter, "background") == ConversationStatusIcon.Conversation);

        // Assert
        Assert.Equal(before, catalog.Snapshot);
        Assert.All(presenter.Snapshot, item => Assert.Equal(ConversationStatusGroup.Other, item.StatusGroup));
    }

    [Theory]
    [InlineData("permission", ConversationStatusIcon.Permission)]
    [InlineData("question", ConversationStatusIcon.Input)]
    [InlineData("elicitation", ConversationStatusIcon.Input)]
    [InlineData("failure", ConversationStatusIcon.Error)]
    public async Task NewInterventionDuringLongTurn_ProjectsNewActivityAheadOfOlderAttention(
        string intervention, ConversationStatusIcon expectedIcon)
    {
        // Arrange
        var dispatcher = new PumpDispatcher();
        var catalog = new ConversationCatalogPresenter();
        var startedAt = DateTime.UtcNow.AddHours(-2);
        var olderReplyAt = startedAt.AddHours(1);
        var initial = ChatState.Empty with
        {
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty
                .Add("long-turn", new("long-turn", "turn-a", ChatTurnPhase.Thinking, startedAt, startedAt))
                .Add("older-reply", new("older-reply", "turn-b", ChatTurnPhase.Completed, olderReplyAt, olderReplyAt))
        };
        await using var attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
        await using var chatState = State.Value(new object(), () => ChatState.Empty);
        var chat = new ChatStore(chatState);
        var attention = new ConversationAttentionStore(attentionState);
        var panels = new ChatConversationPanelStateCoordinator();
        using var presenter = new ConversationCatalogDisplayPresenter(catalog,
            attention, dispatcher, chat, panels);
        catalog.Refresh([CatalogItem("long-turn"), CatalogItem("older-reply")]);
        await chatState.Update(_ => initial, TestContext.Current.CancellationToken);
        await attention.Dispatch(new MarkConversationUnreadAction("older-reply", ConversationAttentionSource.AgentMessage, olderReplyAt));
        await dispatcher.UntilAsync(() => Status(presenter, "long-turn") == ConversationStatusIcon.Working
            && Status(presenter, "older-reply") == ConversationStatusIcon.Unread);

        // Act
        var interventionAt = DateTime.UtcNow;
        switch (intervention)
        {
            case "permission": panels.StorePermissionRequest("long-turn", new PermissionRequestViewModel()); break;
            case "question": panels.StoreAskUserRequest("long-turn", new("question", "remote", "Choose", [])); break;
            case "elicitation": panels.TryStoreElicitationRequest("long-turn", new ElicitationRequestViewModel("form", "remote", "Choose", [])); break;
            case "failure": await chat.Dispatch(new SetConversationOperationFailureAction(new("long-turn", "Reconnect"))); break;
        }
        await dispatcher.UntilAsync(() => Status(presenter, "long-turn") == expectedIcon);

        // Assert: the navigation sort input uses the new intervention, not the old turn start.
        var active = presenter.Snapshot.Single(item => item.ConversationId == "long-turn");
        Assert.InRange(active.ActivityAt!.Value, interventionAt, DateTime.UtcNow);
        Assert.Equal(new[] { "long-turn", "older-reply" }, presenter.Snapshot
            .Where(item => item.StatusGroup == ConversationStatusGroup.NeedsAttention)
            .OrderByDescending(item => item.ActivityAt).Select(item => item.ConversationId));
    }

    [Fact]
    public async Task RunningUnreadChunks_PreserveGroupAndActivity_AndSuppressEquivalentProjection()
    {
        // Arrange
        var dispatcher = new PumpDispatcher();
        var catalog = new ConversationCatalogPresenter();
        await using var attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
        await using var chatState = State.Value(new object(), () => ChatState.Empty);
        var attention = new ConversationAttentionStore(attentionState);
        var chat = new ChatStore(chatState);
        using var presenter = new ConversationCatalogDisplayPresenter(catalog, attention, dispatcher, chat);
        catalog.Refresh([CatalogItem("conversation")]);
        dispatcher.Drain();
        await chat.Dispatch(new BeginTurnAction("conversation", "turn", ChatTurnPhase.Responding));
        await dispatcher.UntilAsync(() => Status(presenter, "conversation") == ConversationStatusIcon.Working);
        await attention.Dispatch(new MarkConversationUnreadAction("conversation", ConversationAttentionSource.AgentMessage, DateTime.UtcNow));
        await dispatcher.UntilAsync(() => IsUnread(presenter, "conversation"));
        var first = Assert.Single(presenter.Snapshot);
        var snapshots = new List<ConversationCatalogDisplayItem>();
        presenter.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(presenter.Snapshot)) snapshots.Add(Assert.Single(presenter.Snapshot));
        };

        // Act
        for (var i = 1; i <= 4; i++)
        {
            var before = dispatcher.Executed;
            await attention.Dispatch(new MarkConversationUnreadAction("conversation", ConversationAttentionSource.AgentMessage, DateTime.UtcNow.AddSeconds(i)));
            await dispatcher.UntilAsync(() => dispatcher.Executed > before);
        }

        // Assert
        var current = Assert.Single(presenter.Snapshot);
        Assert.Equal(ConversationStatusGroup.Working, current.StatusGroup);
        Assert.Equal(ConversationStatusIcon.Working, current.StatusIcon);
        Assert.Equal(first.ActivityAt, current.ActivityAt);
        Assert.Empty(snapshots);
    }

    [Fact]
    public async Task CatalogRefresh_EquivalentFacts_DoesNotRepublishSnapshot()
    {
        // Arrange
        var dispatcher = new PumpDispatcher();
        var catalog = new ConversationCatalogPresenter();
        await using var attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
        var attention = new ConversationAttentionStore(attentionState);
        using var presenter = new ConversationCatalogDisplayPresenter(catalog, attention, dispatcher);
        var item = CatalogItem("conversation");
        catalog.Refresh([item]);
        dispatcher.Drain();
        var snapshotChanges = 0;
        presenter.PropertyChanged += (_, args) => snapshotChanges += args.PropertyName == nameof(presenter.Snapshot) ? 1 : 0;

        // Act
        catalog.Refresh([item with { }]);
        dispatcher.Drain();

        // Assert
        Assert.Equal(0, snapshotChanges);
        Assert.Equal(item.DisplayName, Assert.Single(presenter.Snapshot).DisplayName);
    }

    [Fact]
    public async Task Dispose_WithQueuedLiveUpdate_DoesNotPublishOrKeepPendingRequestSubscription()
    {
        // Arrange
        var dispatcher = new PumpDispatcher();
        var catalog = new ConversationCatalogPresenter();
        await using var attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
        var attention = new ConversationAttentionStore(attentionState);
        var panels = new ChatConversationPanelStateCoordinator();
        using var presenter = new ConversationCatalogDisplayPresenter(catalog, attention, dispatcher, panels: panels);
        catalog.Refresh([CatalogItem("conversation")]);
        dispatcher.Drain();
        var changed = 0;
        presenter.PropertyChanged += (_, _) => changed++;

        // Act
        panels.StoreAskUserRequest("conversation", new AskUserRequestViewModel("request", "remote", "Question", []));
        presenter.Dispose();
        dispatcher.Drain();
        panels.RemoveAskUserRequest("conversation");
        dispatcher.Drain();

        // Assert
        Assert.Equal(0, changed);
        Assert.Equal(ConversationStatusIcon.Conversation, Assert.Single(presenter.Snapshot).StatusIcon);
    }

    [Fact]
    public async Task TranscriptReset_WithUnchangedTurn_DoesNotRepublishNavigationButCompletionStillUpdatesIt()
    {
        var dispatcher = new PumpDispatcher();
        var catalog = new ConversationCatalogPresenter();
        await using var attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
        await using var chatState = State.Value(new object(), () => ChatState.Empty);
        var chat = new ChatStore(chatState);
        using var presenter = new ConversationCatalogDisplayPresenter(catalog,
            new ConversationAttentionStore(attentionState), dispatcher, chat);
        catalog.Refresh([CatalogItem("conversation")]);
        await chat.Dispatch(new BeginTurnAction("conversation", "turn", ChatTurnPhase.Thinking));
        await dispatcher.UntilAsync(() => Status(presenter, "conversation") == ConversationStatusIcon.Working);
        var initialSnapshot = presenter.Snapshot;
        var snapshots = new List<ConversationCatalogDisplayItem>();
        presenter.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(presenter.Snapshot)) snapshots.Add(Assert.Single(presenter.Snapshot));
        };
        await chat.Dispatch(new UpsertTranscriptMessageAction("conversation", new ConversationMessageSnapshot
        {
            Id = "message",
            ContentType = "text",
            TextContent = "reply"
        }));

        var resetObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        chatState.ForEach((current, _) =>
        {
            if (current?.ResolveContentSlice("conversation")?.Transcript.Count == 0
                && current.DraftRevision > 0) resetObserved.TrySetResult();
            return ValueTask.CompletedTask;
        }, out var subscription);
        try
        {
            await chat.Dispatch(new HydrateConversationAction("conversation", ImmutableList<ConversationMessageSnapshot>.Empty,
                ImmutableList<ConversationPlanEntrySnapshot>.Empty, false));
            await chat.Dispatch(new SetDraftTextAction(string.Empty));
            await resetObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            subscription?.Dispose();
        }
        dispatcher.Drain();

        Assert.Same(initialSnapshot, presenter.Snapshot);
        Assert.Empty(snapshots);
        Assert.Empty((await chat.GetCurrentStateAsync()).ResolveContentSlice("conversation")!.Value.Transcript);

        await chat.Dispatch(new CompleteTurnAction("conversation", "turn", "end_turn", HasStopReason: true));
        await dispatcher.UntilAsync(() => Status(presenter, "conversation") == ConversationStatusIcon.Conversation);

        var completed = Assert.Single(snapshots);
        Assert.Equal(ConversationStatusGroup.Other, completed.StatusGroup);
        Assert.Equal((await chat.GetCurrentStateAsync()).ResolveTurn("conversation")!.LastUpdatedAtUtc, completed.ActivityAt);
    }

    private static ConversationStatusIcon Status(ConversationCatalogDisplayPresenter presenter, string conversationId)
        => presenter.Snapshot.Single(item => item.ConversationId == conversationId).StatusIcon;

    private static ConversationCatalogItem CatalogItem(string conversationId)
    {
        var stamp = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        return new ConversationCatalogItem(conversationId, conversationId, "/repo/demo", stamp, stamp, stamp);
    }

    private sealed class PumpDispatcher : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        public bool HasThreadAccess => false;
        public int Executed { get; private set; }
        public void Enqueue(Action action) => _queue.Enqueue(action);

        public Task EnqueueAsync(Action action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(() =>
            {
                try { action(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
            });
            return completion.Task;
        }

        public Task EnqueueAsync(Func<Task> function)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(async () =>
            {
                try { await function(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
            });
            return completion.Task;
        }

        public void Drain()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
                Executed++;
            }
        }

        public async Task UntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            do
            {
                Drain();
                if (condition()) return;
                await Task.Delay(10, TestContext.Current.CancellationToken);
            } while (DateTime.UtcNow < deadline);
            Assert.True(condition(), "The live presenter did not reach the expected state.");
        }
    }

    private static bool IsUnread(ConversationCatalogDisplayPresenter presenter, string conversationId)
        => presenter.Snapshot.FirstOrDefault(item => string.Equals(item.ConversationId, conversationId, StringComparison.Ordinal))?.HasUnreadAttention == true;

    private static async Task WaitForConditionAsync(Func<bool> predicate, int attempts = 3000, int delayMs = 10)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(delayMs);
        }

        Assert.True(predicate());
    }
}
