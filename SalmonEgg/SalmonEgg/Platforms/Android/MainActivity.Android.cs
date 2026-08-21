using System;
using System.Threading;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content.PM;
using Android.Content;
using Android.OS;
using Android.Views;
using SalmonEgg.Domain.Services;
using SalmonEgg.Platforms.Android;

namespace SalmonEgg.Droid;

[Activity(
    MainLauncher = true,
    ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden
)]
public class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
    private const int NotificationPermissionRequestCode = 4107;
    private static readonly object PermissionSync = new();
    private static readonly object ActivationSync = new();
    private static TaskCompletionSource<bool>? _notificationPermissionSource;

    // A tap can launch the process, so the intent may arrive before anything is listening. The latest
    // one is parked until the notification service starts, then drained.
    private static SystemNotificationActivatedEventArgs? _pendingActivation;

    /// <summary>Raised when the user taps a notification this app posted.</summary>
    public static event EventHandler<SystemNotificationActivatedEventArgs>? NotificationActivated;

    /// <summary>Replays a tap that arrived before the notification service began listening.</summary>
    public static void DrainPendingNotificationActivation()
    {
        SystemNotificationActivatedEventArgs? pending;
        lock (ActivationSync)
        {
            pending = _pendingActivation;
            _pendingActivation = null;
        }

        if (pending is not null)
        {
            NotificationActivated?.Invoke(null, pending);
        }
    }

    private static void PublishNotificationActivation(Intent? intent)
    {
        var notificationId = intent?.GetStringExtra(AndroidSystemNotificationService.NotificationIdExtra);
        if (string.IsNullOrWhiteSpace(notificationId))
        {
            return;
        }

        var conversationId = intent?.GetStringExtra(AndroidSystemNotificationService.ConversationIdExtra);
        var activation = new SystemNotificationActivatedEventArgs(notificationId, conversationId);

        var handler = NotificationActivated;
        if (handler is null)
        {
            lock (ActivationSync)
            {
                _pendingActivation = activation;
            }

            return;
        }

        handler.Invoke(null, activation);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

        base.OnCreate(savedInstanceState);

        // Cold launch by notification tap: the activation is on the launch intent.
        PublishNotificationActivation(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);

        // Warm tap: SingleTop delivers it here rather than recreating the activity.
        PublishNotificationActivation(intent);
    }

    protected override void OnDestroy()
    {
        lock (PermissionSync)
        {
            _notificationPermissionSource?.TrySetResult(false);
            _notificationPermissionSource = null;
        }

        base.OnDestroy();
    }

    public override void OnRequestPermissionsResult(
        int requestCode,
        string[]? permissions,
        [global::Android.Runtime.GeneratedEnum] Permission[]? grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != NotificationPermissionRequestCode)
        {
            return;
        }

        var granted = grantResults is { Length: > 0 }
            && grantResults[0] == Permission.Granted;
        lock (PermissionSync)
        {
            var permissionSource = _notificationPermissionSource;
            _notificationPermissionSource = null;
            permissionSource?.TrySetResult(granted);
        }
    }

    public static Task<bool> RequestNotificationPermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Uno's BaseActivity is the authoritative current-activity owner; this class must not keep
        // a second copy of that fact.
        if (global::Uno.UI.BaseActivity.Current is not MainActivity activity)
        {
            return Task.FromResult(false);
        }

        Task<bool> permissionTask;
        lock (PermissionSync)
        {
            if (_notificationPermissionSource is not null)
            {
                permissionTask = _notificationPermissionSource.Task;
            }
            else
            {
                _notificationPermissionSource = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                permissionTask = _notificationPermissionSource.Task;
                activity.RunOnUiThread(() =>
                    activity.RequestPermissions(
                        new[] { Manifest.Permission.PostNotifications },
                        NotificationPermissionRequestCode));
            }
        }

        return permissionTask.WaitAsync(cancellationToken);
    }
}
