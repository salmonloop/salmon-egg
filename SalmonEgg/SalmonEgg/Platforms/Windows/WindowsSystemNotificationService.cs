#if WINDOWS
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Platforms.Windows;

public sealed class WindowsSystemNotificationService : ISystemNotificationService, ISystemNotificationActivationSource
{
    private const string NotificationIdArgument = "notificationId";
    private const string ConversationIdArgument = "conversationId";

    private readonly object _sync = new();
    private bool _isRegistered;
    private bool _isListening;

    public event EventHandler<SystemNotificationActivatedEventArgs>? Activated;

    public void Start()
    {
        lock (_sync)
        {
            if (_isListening)
            {
                return;
            }

            _isListening = true;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            EnsureRegistered();
        }
        catch
        {
            // A host without the notification stack cannot report activations. Posting already
            // reports Unsupported, so there is nothing further to surface here.
            lock (_sync)
            {
                _isListening = false;
            }
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        args.Arguments.TryGetValue(NotificationIdArgument, out var notificationId);
        args.Arguments.TryGetValue(ConversationIdArgument, out var conversationId);
        if (string.IsNullOrWhiteSpace(notificationId))
        {
            return;
        }

        Activated?.Invoke(this, new SystemNotificationActivatedEventArgs(notificationId, conversationId));
    }

    public bool IsSupported => AppNotificationManager.IsSupported();

    public Task<SystemNotificationPermissionResult> RequestPermissionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            IsSupported
                ? SystemNotificationPermissionResult.Granted
                : SystemNotificationPermissionResult.Unsupported);
    }

    public Task<SystemNotificationResult> ShowAsync(
        SystemNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.IsValid)
        {
            return Task.FromResult(SystemNotificationResult.Failed);
        }

        if (!IsSupported)
        {
            return Task.FromResult(SystemNotificationResult.Unsupported);
        }

        try
        {
            EnsureRegistered();
            // Arguments come back verbatim in NotificationInvoked, which is how a tap routes.
            var builder = new AppNotificationBuilder()
                .AddArgument(NotificationIdArgument, request.NotificationId)
                .AddText(request.Title.Trim())
                .AddText(request.Body.Trim());
            if (!string.IsNullOrWhiteSpace(request.ConversationId))
            {
                builder.AddArgument(ConversationIdArgument, request.ConversationId.Trim());
            }

            var notification = builder.BuildNotification();

            // Tag is the platform's addressable identity for this notification (see
            // AppNotificationManager.RemoveByTagAsync), so one turn maps to one notification.
            notification.Tag = request.NotificationId;

            AppNotificationManager.Default.Show(notification);
            return Task.FromResult(SystemNotificationResult.Shown);
        }
        catch
        {
            return Task.FromResult(SystemNotificationResult.Failed);
        }
    }

    // Registration is deferred to the first notification instead of running in the constructor,
    // because DI construction must stay free of platform side effects.
    private void EnsureRegistered()
    {
        lock (_sync)
        {
            if (_isRegistered)
            {
                return;
            }

            AppNotificationManager.Default.Register();
            _isRegistered = true;
        }
    }
}
#endif
