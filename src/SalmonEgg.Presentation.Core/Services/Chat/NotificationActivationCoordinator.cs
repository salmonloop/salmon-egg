using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Turns a tapped system notification into an authoritative conversation activation.
/// </summary>
/// <remarks>
/// A notification tap can arrive before the conversation catalog exists — the platform may have
/// launched the app to deliver it — so an activation waits for conversation restore rather than being
/// dropped as "unknown conversation". Only the newest tap is honoured while waiting: the user's last
/// choice is the one they meant.
/// </remarks>
public sealed class NotificationActivationCoordinator : IConversationRestoreCompletionSink, IDisposable
{
    private readonly ISystemNotificationActivationSource _activationSource;
    private readonly IConversationOpenRouter _openRouter;
    private readonly ILogger<NotificationActivationCoordinator> _logger;
    private readonly object _sync = new();

    private readonly TaskCompletionSource<bool> _conversationsRestored =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private long _latestActivationVersion;
    private bool _disposed;

    public NotificationActivationCoordinator(
        ISystemNotificationActivationSource activationSource,
        IConversationOpenRouter openRouter,
        ILogger<NotificationActivationCoordinator> logger)
    {
        _activationSource = activationSource ?? throw new ArgumentNullException(nameof(activationSource));
        _openRouter = openRouter ?? throw new ArgumentNullException(nameof(openRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribing is not a platform side effect; the source only starts listening on Start().
        _activationSource.Activated += OnActivated;
    }

    /// <summary>
    /// Begins listening for notification activations. The application startup workflow owns this call.
    /// </summary>
    public void Start()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        _activationSource.Start();
    }

    public void OnConversationRestoreCompleted(bool restored)
        => _conversationsRestored.TrySetResult(restored);

    private void OnActivated(object? sender, SystemNotificationActivatedEventArgs e)
        => _ = ActivateAsync(e);

    private async Task ActivateAsync(SystemNotificationActivatedEventArgs activation)
    {
        var conversationId = activation.ConversationId?.Trim();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            // Tapping still brought the app forward, which is the platform's own behaviour. There is
            // simply nothing extra to route.
            return;
        }

        var activationVersion = Interlocked.Increment(ref _latestActivationVersion);

        // The catalog may not exist yet on a launch-by-notification, so wait for the restore the
        // startup workflow owns rather than racing it and reporting a real conversation as unknown.
        var restored = await _conversationsRestored.Task.ConfigureAwait(false);

        if (Volatile.Read(ref _latestActivationVersion) != activationVersion)
        {
            // A newer tap arrived while waiting. That one is the user's current intent.
            return;
        }

        if (_disposed)
        {
            return;
        }

        if (!restored)
        {
            _logger.LogWarning(
                "Notification activation could not open a conversation because restore failed. NotificationId={NotificationId} ConversationId={ConversationId}",
                activation.NotificationId,
                conversationId);
            return;
        }

        var result = await _openRouter.OpenConversationAsync(conversationId).ConfigureAwait(false);
        if (result is not ConversationOpenResult.Opened)
        {
            _logger.LogInformation(
                "Notification activation did not open a conversation. NotificationId={NotificationId} ConversationId={ConversationId} Result={Result}",
                activation.NotificationId,
                conversationId,
                result);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _activationSource.Activated -= OnActivated;
    }
}
