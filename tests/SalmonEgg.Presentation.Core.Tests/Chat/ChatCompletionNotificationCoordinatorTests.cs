using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

// Every negative case proves the pipeline actually drained past the suppressed turn by completing a
// later turn that must notify, instead of waiting a fixed delay and calling silence a pass.
[Collection("NonParallel")]
public sealed class ChatCompletionNotificationCoordinatorTests
{
    private const string FirstNotificationId = "turn:conversation-1:turn-1";
    private const string SecondNotificationId = "turn:conversation-1:turn-2";

    [Fact]
    public async Task CompletedTurn_WhenApplicationIsBackgrounded_ShowsOneNotification()
    {
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var store = new ChatStore(state);
        var notifications = new RecordingNotificationService();
        var visibility = new FakeVisibilityState { IsActive = false };
        using var coordinator = new ChatCompletionNotificationCoordinator(
            store,
            notifications,
            new FakeNotificationSettings { SystemNotificationsEnabled = true },
            visibility,
            CreateLocalizer(),
            NullLogger<ChatCompletionNotificationCoordinator>.Instance);

        await CompleteTurnAsync(store, "turn-1");

        var shown = await WaitForNotificationAsync(notifications, FirstNotificationId);

        Assert.Equal(new[] { FirstNotificationId }, shown.Select(request => request.NotificationId));
        Assert.Equal("Task completed", shown[0].Title);
        Assert.Equal("The agent finished responding.", shown[0].Body);
    }

    [Fact]
    public async Task CompletedTurn_WhenApplicationIsActive_DoesNotShowNotification()
    {
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var store = new ChatStore(state);
        var notifications = new RecordingNotificationService();
        var visibility = new FakeVisibilityState { IsActive = true };
        using var coordinator = new ChatCompletionNotificationCoordinator(
            store,
            notifications,
            new FakeNotificationSettings { SystemNotificationsEnabled = true },
            visibility,
            CreateLocalizer(),
            NullLogger<ChatCompletionNotificationCoordinator>.Instance);

        await CompleteTurnAsync(store, "turn-1");

        visibility.IsActive = false;
        await CompleteTurnAsync(store, "turn-2");
        var shown = await WaitForNotificationAsync(notifications, SecondNotificationId);

        Assert.Equal(new[] { SecondNotificationId }, shown.Select(request => request.NotificationId));
    }

    [Fact]
    public async Task FirstObservedStateAlreadyCompleted_DoesNotShowNotification()
    {
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var store = new ChatStore(state);
        var notifications = new RecordingNotificationService();
        using var coordinator = new ChatCompletionNotificationCoordinator(
            store,
            notifications,
            new FakeNotificationSettings { SystemNotificationsEnabled = true },
            new FakeVisibilityState { IsActive = false },
            CreateLocalizer(),
            NullLogger<ChatCompletionNotificationCoordinator>.Instance);

        // A single dispatch that lands straight in Completed reproduces what a restored session looks
        // like: the very first state this coordinator ever sees already holds a completed turn, with
        // no earlier phase to compare against. Nothing completed while the user was away, so no
        // notification is owed.
        await store.Dispatch(new BeginTurnAction("conversation-1", "turn-1", ChatTurnPhase.Completed));

        await CompleteTurnAsync(store, "turn-2");
        var shown = await WaitForNotificationAsync(notifications, SecondNotificationId);

        Assert.Equal(new[] { SecondNotificationId }, shown.Select(request => request.NotificationId));
    }

    [Fact]
    public async Task CompletedTurn_WhenNotificationsAreDisabled_DoesNotShowNotification()
    {
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var store = new ChatStore(state);
        var notifications = new RecordingNotificationService();
        var settings = new FakeNotificationSettings { SystemNotificationsEnabled = false };
        using var coordinator = new ChatCompletionNotificationCoordinator(
            store,
            notifications,
            settings,
            new FakeVisibilityState { IsActive = false },
            CreateLocalizer(),
            NullLogger<ChatCompletionNotificationCoordinator>.Instance);

        await CompleteTurnAsync(store, "turn-1");

        settings.SystemNotificationsEnabled = true;
        await CompleteTurnAsync(store, "turn-2");
        var shown = await WaitForNotificationAsync(notifications, SecondNotificationId);

        Assert.Equal(new[] { SecondNotificationId }, shown.Select(request => request.NotificationId));
    }

    [Fact]
    public async Task UnsupportedPlatform_DoesNotShowNotification()
    {
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var store = new ChatStore(state);
        var notifications = new RecordingNotificationService { IsSupported = false };
        using var coordinator = new ChatCompletionNotificationCoordinator(
            store,
            notifications,
            new FakeNotificationSettings { SystemNotificationsEnabled = true },
            new FakeVisibilityState { IsActive = false },
            CreateLocalizer(),
            NullLogger<ChatCompletionNotificationCoordinator>.Instance);

        await CompleteTurnAsync(store, "turn-1");

        notifications.IsSupported = true;
        await CompleteTurnAsync(store, "turn-2");
        var shown = await WaitForNotificationAsync(notifications, SecondNotificationId);

        Assert.Equal(new[] { SecondNotificationId }, shown.Select(request => request.NotificationId));
    }

    [Fact]
    public async Task SameTurnCompletedAgainAfterClear_DoesNotDuplicateNotification()
    {
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var store = new ChatStore(state);
        var notifications = new RecordingNotificationService();
        using var coordinator = new ChatCompletionNotificationCoordinator(
            store,
            notifications,
            new FakeNotificationSettings { SystemNotificationsEnabled = true },
            new FakeVisibilityState { IsActive = false },
            CreateLocalizer(),
            NullLogger<ChatCompletionNotificationCoordinator>.Instance);

        await CompleteTurnAsync(store, "turn-1");
        await WaitForNotificationAsync(notifications, FirstNotificationId);

        // Clearing the terminal turn and re-observing it as completed (what a re-projection or
        // conversation switch back looks like) leaves no earlier phase to compare against, so the
        // phase-edge policy alone would notify again. The per-turn reservation is what stops it.
        await store.Dispatch(new ClearTerminalTurnAction("conversation-1"));
        await store.Dispatch(new BeginTurnAction("conversation-1", "turn-1", ChatTurnPhase.Completed));

        await CompleteTurnAsync(store, "turn-2");
        var shown = await WaitForNotificationAsync(notifications, SecondNotificationId);

        Assert.Equal(
            new[] { FirstNotificationId, SecondNotificationId },
            shown.Select(request => request.NotificationId));
    }

    [Fact]
    public async Task UnrelatedStateChangeAfterCompletion_DoesNotDuplicateNotification()
    {
        await using var state = State.Value(new object(), () => ChatState.Empty);
        var store = new ChatStore(state);
        var notifications = new RecordingNotificationService();
        using var coordinator = new ChatCompletionNotificationCoordinator(
            store,
            notifications,
            new FakeNotificationSettings { SystemNotificationsEnabled = true },
            new FakeVisibilityState { IsActive = false },
            CreateLocalizer(),
            NullLogger<ChatCompletionNotificationCoordinator>.Instance);

        await CompleteTurnAsync(store, "turn-1");
        await WaitForNotificationAsync(notifications, FirstNotificationId);

        // An unrelated mutation re-publishes the same completed turn. Note the reducer already drops
        // a second CompleteTurnAction on a terminal turn, so this is the realistic duplicate shape.
        await store.Dispatch(new SetDraftTextAction("anything"));
        await CompleteTurnAsync(store, "turn-2");
        var shown = await WaitForNotificationAsync(notifications, SecondNotificationId);

        Assert.Equal(
            new[] { FirstNotificationId, SecondNotificationId },
            shown.Select(request => request.NotificationId));
    }

    [Fact]
    public void FailedTurn_IsNotACompletionTransition()
    {
        var previous = new ActiveTurnState(
            "conversation-1",
            "turn-1",
            ChatTurnPhase.WaitingForAgent,
            DateTime.UtcNow,
            DateTime.UtcNow);
        var current = previous with { Phase = ChatTurnPhase.Failed };

        Assert.False(ChatCompletionNotificationPolicy.IsCompletedTransition(previous, current));
    }

    [Theory]
    [InlineData(ChatTurnPhase.Cancelled)]
    [InlineData(ChatTurnPhase.WaitingForAgent)]
    public void NonCompletedPhase_IsNotACompletionTransition(ChatTurnPhase phase)
    {
        var previous = new ActiveTurnState(
            "conversation-1",
            "turn-1",
            ChatTurnPhase.WaitingForAgent,
            DateTime.UtcNow,
            DateTime.UtcNow);

        Assert.False(ChatCompletionNotificationPolicy.IsCompletedTransition(previous, previous with { Phase = phase }));
    }

    [Fact]
    public void AlreadyCompletedTurn_IsNotACompletionTransition()
    {
        var completed = new ActiveTurnState(
            "conversation-1",
            "turn-1",
            ChatTurnPhase.Completed,
            DateTime.UtcNow,
            DateTime.UtcNow);

        Assert.False(ChatCompletionNotificationPolicy.IsCompletedTransition(completed, completed));
        Assert.True(ChatCompletionNotificationPolicy.IsCompletedTransition(
            completed,
            completed with { TurnId = "turn-2" }));
    }

    private static async Task CompleteTurnAsync(ChatStore store, string turnId)
    {
        await store.Dispatch(new BeginTurnAction("conversation-1", turnId, ChatTurnPhase.WaitingForAgent));
        await store.Dispatch(new CompleteTurnAction("conversation-1", turnId));
    }

    private static async Task<IReadOnlyList<SystemNotificationRequest>> WaitForNotificationAsync(
        RecordingNotificationService notifications,
        string notificationId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var shown = notifications.Requests;
            if (shown.Any(request => string.Equals(request.NotificationId, notificationId, StringComparison.Ordinal)))
            {
                return shown;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Fail(
            $"'{notificationId}' was never shown. Shown: [{string.Join(", ", notifications.Requests.Select(request => request.NotificationId))}]");
        return Array.Empty<SystemNotificationRequest>();
    }

    private static IStringLocalizer<CoreStrings> CreateLocalizer()
    {
        var localizer = new TestLocalizer();
        localizer.Values["SystemNotification_TurnCompletedTitle"] = "Task completed";
        localizer.Values["SystemNotification_TurnCompletedBody"] = "The agent finished responding.";
        return localizer;
    }

    private sealed class FakeVisibilityState : IApplicationVisibilityState
    {
        public bool IsActive { get; set; }
    }

    private sealed class FakeNotificationSettings : IApplicationNotificationSettings
    {
        public bool SystemNotificationsEnabled { get; set; }
    }

    private sealed class RecordingNotificationService : ISystemNotificationService
    {
        private readonly ConcurrentQueue<SystemNotificationRequest> _requests = new();

        public bool IsSupported { get; set; } = true;

        public IReadOnlyList<SystemNotificationRequest> Requests => _requests.ToArray();

        public Task<SystemNotificationPermissionResult> RequestPermissionAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(SystemNotificationPermissionResult.Granted);

        public Task<SystemNotificationResult> ShowAsync(
            SystemNotificationRequest request,
            CancellationToken cancellationToken = default)
        {
            _requests.Enqueue(request);
            return Task.FromResult(SystemNotificationResult.Shown);
        }
    }

    private sealed class TestLocalizer : IStringLocalizer<CoreStrings>
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public LocalizedString this[string name]
            => new(name, Values.TryGetValue(name, out var value) ? value : name);

        public LocalizedString this[string name, params object[] arguments]
            => new(name, string.Format(this[name].Value, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => Array.Empty<LocalizedString>();
    }
}
