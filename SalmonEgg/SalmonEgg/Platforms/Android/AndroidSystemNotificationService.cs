#if __ANDROID__
using System;
using System.Threading;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Platforms.Android;

public sealed class AndroidSystemNotificationService : ISystemNotificationService, ISystemNotificationActivationSource
{
    private const string ChannelId = "agent-completions";

    // Intent extras the launch intent carries back when the user taps a notification.
    public const string NotificationIdExtra = "salmonegg.notificationId";
    public const string ConversationIdExtra = "salmonegg.conversationId";

    // Notification identity is (tag, id) and the per-turn tag is what varies, so one shared numeric
    // slot is enough. It is deliberately not 0: that value is a common sentinel elsewhere in the
    // platform and in third-party code, which invites collisions.
    private const int NotificationSlotId = 1;

    private readonly Context _context;
    private readonly IStringLocalizer<CoreStrings> _localizer;

    public AndroidSystemNotificationService(IStringLocalizer<CoreStrings> localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _context = global::Android.App.Application.Context
            ?? throw new InvalidOperationException("Android application context is unavailable.");
    }

    public event EventHandler<SystemNotificationActivatedEventArgs>? Activated;

    public void Start()
    {
        // MainActivity receives taps as intents, both for a cold launch and via OnNewIntent, and
        // forwards them here. There is no separate native listener to attach.
        global::SalmonEgg.Droid.MainActivity.NotificationActivated += OnNotificationActivated;
        global::SalmonEgg.Droid.MainActivity.DrainPendingNotificationActivation();
    }

    private void OnNotificationActivated(object? sender, SystemNotificationActivatedEventArgs e)
        => Activated?.Invoke(this, e);

    public bool IsSupported => true;

    public Task<SystemNotificationPermissionResult> RequestPermissionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
        {
            return Task.FromResult(IsNotificationsEnabled()
                ? SystemNotificationPermissionResult.Granted
                : SystemNotificationPermissionResult.Denied);
        }

        if (ContextCompat.CheckSelfPermission(_context, Manifest.Permission.PostNotifications) == Permission.Granted)
        {
            return Task.FromResult(SystemNotificationPermissionResult.Granted);
        }

        return RequestAndroidPermissionAsync(cancellationToken);
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

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            && ContextCompat.CheckSelfPermission(_context, Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            return Task.FromResult(SystemNotificationResult.Denied);
        }

        try
        {
            EnsureChannel();
            var notifications = NotificationManagerCompat.From(_context);
            if (notifications is null || !notifications.AreNotificationsEnabled())
            {
                return Task.FromResult(SystemNotificationResult.Denied);
            }

            // The AndroidX builder setters are bound as nullable-returning, so they are called as
            // statements rather than chained.
            var builder = new NotificationCompat.Builder(_context, ChannelId);
            builder.SetSmallIcon(global::SalmonEgg.Resource.Drawable.ic_notification);
            builder.SetContentTitle(request.Title.Trim());
            builder.SetContentText(request.Body.Trim());
            builder.SetPriority((int)NotificationPriority.Default);
            builder.SetAutoCancel(true);

            var launchIntent = _context.PackageManager?.GetLaunchIntentForPackage(_context.PackageName!);
            if (launchIntent is not null)
            {
                // Tapping brings the running task to the front and carries the turn's identity so
                // the shared layer can route to the conversation.
                launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
                launchIntent.PutExtra(NotificationIdExtra, request.NotificationId);
                if (!string.IsNullOrWhiteSpace(request.ConversationId))
                {
                    launchIntent.PutExtra(ConversationIdExtra, request.ConversationId.Trim());
                }

                // A distinct request code per turn keeps each turn's extras intact; UpdateCurrent on a
                // shared code would rewrite an earlier notification's payload.
                var pendingIntent = PendingIntent.GetActivity(
                    _context,
                    LaunchRequestCode(request.NotificationId),
                    launchIntent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
                builder.SetContentIntent(pendingIntent);
            }

            var notification = builder.Build();
            if (notification is null)
            {
                return Task.FromResult(SystemNotificationResult.Failed);
            }

            // The string tag keys the notification, so re-notifying one turn replaces it instead of
            // stacking. A process-local numeric hash would not survive a restart.
            notifications.Notify(request.NotificationId, NotificationSlotId, notification);
            return Task.FromResult(SystemNotificationResult.Shown);
        }
        catch
        {
            return Task.FromResult(SystemNotificationResult.Failed);
        }
    }

    // Re-creating an existing channel is a documented no-op apart from refreshing its user-visible
    // name, so this is not cached: it keeps the channel label in sync after a language change.
    private void EnsureChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager?)_context.GetSystemService(Context.NotificationService);
        if (manager is null)
        {
            return;
        }

        manager.CreateNotificationChannel(
            new NotificationChannel(
                ChannelId,
                _localizer["SystemNotification_ChannelName"].Value,
                NotificationImportance.Default));
    }

    // PendingIntent identity is (request code, intent), so per-turn codes keep per-turn extras.
    // FNV-1a rather than string.GetHashCode, whose seed is randomized per process: a turn re-notified
    // after a restart must land on the same request code.
    private static int LaunchRequestCode(string notificationId)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var character in notificationId)
        {
            hash = (hash ^ character) * prime;
        }

        return (int)(hash & int.MaxValue);
    }

    private bool IsNotificationsEnabled()
        => NotificationManagerCompat.From(_context) is { } notifications
            && notifications.AreNotificationsEnabled();

    private static async Task<SystemNotificationPermissionResult> RequestAndroidPermissionAsync(
        CancellationToken cancellationToken)
    {
        var granted = await global::SalmonEgg.Droid.MainActivity
            .RequestNotificationPermissionAsync(cancellationToken)
            .ConfigureAwait(false);
        return granted
            ? SystemNotificationPermissionResult.Granted
            : SystemNotificationPermissionResult.Denied;
    }
}
#endif
