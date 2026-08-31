using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class NotificationActivationCoordinatorTests
{
    private const string FirstNotificationId = "turn:conv-1:turn-1";

    [Fact]
    public async Task TappedNotification_AfterRestore_OpensItsConversation()
    {
        // Arrange
        var source = new FakeActivationSource();
        var router = new RecordingOpenRouter(ConversationOpenResult.Opened);
        using var coordinator = CreateCoordinator(source, router);
        coordinator.OnConversationRestoreCompleted(restored: true);

        // Act
        source.RaiseActivated(FirstNotificationId, "conv-1");

        // Assert
        Assert.Equal(new[] { "conv-1" }, await router.WaitForRequestsAsync(1));
    }

    [Fact]
    public async Task TappedNotification_BeforeRestoreCompletes_WaitsInsteadOfReportingUnknown()
    {
        // Arrange — a tap can launch the app, so the catalog may not exist yet.
        var source = new FakeActivationSource();
        var router = new RecordingOpenRouter(ConversationOpenResult.Opened);
        using var coordinator = CreateCoordinator(source, router);

        // Act
        source.RaiseActivated(FirstNotificationId, "conv-1");
        await Task.Delay(30, TestContext.Current.CancellationToken);
        var requestsBeforeRestore = router.Requests;
        coordinator.OnConversationRestoreCompleted(restored: true);

        // Assert
        Assert.Empty(requestsBeforeRestore);
        Assert.Equal(new[] { "conv-1" }, await router.WaitForRequestsAsync(1));
    }

    [Fact]
    public async Task SecondTapWhileWaiting_SupersedesTheFirst()
    {
        // Arrange
        var source = new FakeActivationSource();
        var router = new RecordingOpenRouter(ConversationOpenResult.Opened);
        using var coordinator = CreateCoordinator(source, router);

        // Act — both taps happen before the catalog exists; the newer one is the user's intent.
        source.RaiseActivated(FirstNotificationId, "conv-1");
        source.RaiseActivated("turn:conv-2:turn-1", "conv-2");
        coordinator.OnConversationRestoreCompleted(restored: true);

        // Assert
        var requests = await router.WaitForRequestsAsync(1);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "conv-2" }, requests);
        Assert.Equal(new[] { "conv-2" }, router.Requests);
    }

    [Fact]
    public async Task TappedNotification_WhenRestoreFailed_DoesNotOpenAnything()
    {
        // Arrange
        var source = new FakeActivationSource();
        var router = new RecordingOpenRouter(ConversationOpenResult.Opened);
        var logger = new RecordingLogger<NotificationActivationCoordinator>();
        using var coordinator = new NotificationActivationCoordinator(source, router, logger);

        // Act — restore reports failure, so the catalog is not authoritative.
        source.RaiseActivated(FirstNotificationId, "conv-1");
        coordinator.OnConversationRestoreCompleted(restored: false);

        // Waiting for the declined-activation diagnostic proves the coordinator reached the decision
        // and refused, rather than the assertion merely outrunning a slow pipeline.
        await logger.WaitForEntryAsync("restore failed");

        // Assert
        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task TappedNotification_WithoutAConversation_DoesNotOpenAnything()
    {
        // Arrange — some platforms report a tap with no payload; the app was still brought forward.
        var source = new FakeActivationSource();
        var router = new RecordingOpenRouter(ConversationOpenResult.Opened);
        using var coordinator = CreateCoordinator(source, router);
        coordinator.OnConversationRestoreCompleted(restored: true);

        // Act
        source.RaiseActivated(FirstNotificationId, conversationId: null);
        source.RaiseActivated(FirstNotificationId, conversationId: "   ");
        source.RaiseActivated("turn:conv-2:turn-1", "conv-2");

        // Assert — only the payload-carrying tap routes.
        Assert.Equal(new[] { "conv-2" }, await router.WaitForRequestsAsync(1));
    }

    [Fact]
    public void Start_DelegatesToThePlatformSourceExactlyWhenAsked()
    {
        // Arrange
        var source = new FakeActivationSource();
        using var coordinator = CreateCoordinator(source, new RecordingOpenRouter(ConversationOpenResult.Opened));

        // Assert — construction must not start listening; that is a platform side effect.
        Assert.Equal(0, source.StartCount);

        // Act
        coordinator.Start();

        // Assert
        Assert.Equal(1, source.StartCount);
    }

    [Fact]
    public async Task Dispose_StopsHonouringFurtherTaps()
    {
        // Arrange
        var source = new FakeActivationSource();
        var router = new RecordingOpenRouter(ConversationOpenResult.Opened);
        var coordinator = CreateCoordinator(source, router);
        coordinator.OnConversationRestoreCompleted(restored: true);

        // Assert — subscribed at construction so a tap is never missed between construction and Start.
        Assert.True(source.HasSubscriber);

        // Act
        coordinator.Dispose();

        // Assert — the handler is gone, so a later tap has nowhere to land. This is the fact that
        // matters; a delay would only show the tap had not been processed yet.
        Assert.False(source.HasSubscriber);
        source.RaiseActivated(FirstNotificationId, "conv-1");
        Assert.Empty(router.Requests);

        // A disposed coordinator must not start listening either.
        coordinator.Start();
        Assert.Equal(0, source.StartCount);
    }

    private static NotificationActivationCoordinator CreateCoordinator(
        FakeActivationSource source,
        RecordingOpenRouter router)
        => new(source, router, NullLogger<NotificationActivationCoordinator>.Instance);

    private sealed class FakeActivationSource : ISystemNotificationActivationSource
    {
        public event EventHandler<SystemNotificationActivatedEventArgs>? Activated;

        public int StartCount { get; private set; }

        public bool HasSubscriber => Activated is not null;

        public void Start() => StartCount++;

        public void RaiseActivated(string notificationId, string? conversationId)
            => Activated?.Invoke(this, new SystemNotificationActivatedEventArgs(notificationId, conversationId));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _entries.Enqueue(formatter(state, exception));

        public async Task WaitForEntryAsync(string substring)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (_entries.Any(entry => entry.Contains(substring, StringComparison.Ordinal)))
                {
                    return;
                }

                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.Fail($"No log entry containing '{substring}'. Entries: [{string.Join(" | ", _entries)}]");
        }
    }

    private sealed class RecordingOpenRouter : IConversationOpenRouter
    {
        private readonly ConversationOpenResult _result;
        private readonly ConcurrentQueue<string> _requests = new();

        public RecordingOpenRouter(ConversationOpenResult result)
        {
            _result = result;
        }

        public IReadOnlyList<string> Requests => _requests.ToArray();

        public Task<ConversationOpenResult> OpenConversationAsync(string conversationId)
        {
            _requests.Enqueue(conversationId);
            return Task.FromResult(_result);
        }

        public async Task<IReadOnlyList<string>> WaitForRequestsAsync(int count)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var requests = Requests;
                if (requests.Count >= count)
                {
                    return requests;
                }

                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.Fail($"Expected {count} open request(s); got [{string.Join(", ", Requests)}]");
            return Array.Empty<string>();
        }
    }
}
