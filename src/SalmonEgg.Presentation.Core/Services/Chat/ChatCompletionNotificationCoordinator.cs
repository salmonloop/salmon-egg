using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Projects authoritative completed turns into native notifications when the app is backgrounded.
/// </summary>
public sealed class ChatCompletionNotificationCoordinator : IDisposable
{
    private const int NotificationHistoryLimit = 256;
    private readonly ISystemNotificationService _notificationService;
    private readonly IApplicationNotificationSettings _notificationSettings;
    private readonly IApplicationVisibilityState _visibilityState;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<ChatCompletionNotificationCoordinator> _logger;
    private readonly object _sync = new();
    private readonly HashSet<string> _notifiedTurnIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _notifiedTurnOrder = new();
    private IDisposable? _stateSubscription;
    private bool _disposed;
    private bool _hasObservedState;
    private ActiveTurnState? _previousTurn;

    public ChatCompletionNotificationCoordinator(
        IChatStore chatStore,
        ISystemNotificationService notificationService,
        IApplicationNotificationSettings notificationSettings,
        IApplicationVisibilityState visibilityState,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<ChatCompletionNotificationCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(chatStore);
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _notificationSettings = notificationSettings ?? throw new ArgumentNullException(nameof(notificationSettings));
        _visibilityState = visibilityState ?? throw new ArgumentNullException(nameof(visibilityState));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        chatStore.State.ForEach(
            (state, cancellationToken) => ObserveStateAsync(state, cancellationToken),
            out _stateSubscription);
    }

    private async ValueTask ObserveStateAsync(ChatState? state, CancellationToken cancellationToken)
    {
        if (state is null || cancellationToken.IsCancellationRequested || _disposed)
        {
            return;
        }

        ActiveTurnState? previousTurn;
        lock (_sync)
        {
            previousTurn = _previousTurn;
            _previousTurn = state.ActiveTurn;
            if (!_hasObservedState)
            {
                _hasObservedState = true;
                return;
            }
        }

        var currentTurn = state.ActiveTurn;
        if (currentTurn is null
            || !ChatCompletionNotificationPolicy.IsCompletedTransition(previousTurn, currentTurn)
            || _visibilityState.IsActive
            || !_notificationSettings.SystemNotificationsEnabled
            || !_notificationService.IsSupported)
        {
            return;
        }

        var completedTurn = currentTurn;

        var notificationId = BuildNotificationId(completedTurn);
        lock (_sync)
        {
            if (!_notifiedTurnIds.Add(notificationId))
            {
                return;
            }

            _notifiedTurnOrder.Enqueue(notificationId);
            while (_notifiedTurnOrder.Count > NotificationHistoryLimit)
            {
                _notifiedTurnIds.Remove(_notifiedTurnOrder.Dequeue());
            }
        }

        var request = new SystemNotificationRequest(
            notificationId,
            _localizer["SystemNotification_TurnCompletedTitle"],
            _localizer["SystemNotification_TurnCompletedBody"],
            completedTurn.ConversationId);

        try
        {
            var result = await _notificationService.ShowAsync(request, cancellationToken).ConfigureAwait(false);
            if (result is SystemNotificationResult.Failed)
            {
                ReleaseNotificationReservation(notificationId);
                _logger.LogWarning(
                    "System notification failed. NotificationId={NotificationId} ConversationId={ConversationId}",
                    notificationId,
                    completedTurn.ConversationId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ReleaseNotificationReservation(notificationId);
        }
        catch (Exception ex)
        {
            ReleaseNotificationReservation(notificationId);
            _logger.LogWarning(
                ex,
                "System notification threw unexpectedly. NotificationId={NotificationId} ConversationId={ConversationId}",
                notificationId,
                completedTurn.ConversationId);
        }
    }

    private static string BuildNotificationId(ActiveTurnState turn)
        => $"turn:{turn.ConversationId}:{turn.TurnId}";

    private void ReleaseNotificationReservation(string notificationId)
    {
        lock (_sync)
        {
            if (!_notifiedTurnIds.Remove(notificationId))
            {
                return;
            }

            var retainedIds = new Queue<string>(_notifiedTurnOrder.Count);
            while (_notifiedTurnOrder.TryDequeue(out var queuedId))
            {
                if (!string.Equals(queuedId, notificationId, StringComparison.Ordinal))
                {
                    retainedIds.Enqueue(queuedId);
                }
            }

            while (retainedIds.TryDequeue(out var retainedId))
            {
                _notifiedTurnOrder.Enqueue(retainedId);
            }
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

        _stateSubscription?.Dispose();
        _stateSubscription = null;
    }
}

internal static class ChatCompletionNotificationPolicy
{
    public static bool IsCompletedTransition(ActiveTurnState? previous, ActiveTurnState? current)
        => current is not null
            && current.Phase == ChatTurnPhase.Completed
            && (previous is null
                || previous.Phase != ChatTurnPhase.Completed
                || !string.Equals(previous.TurnId, current.TurnId, StringComparison.Ordinal));
}
