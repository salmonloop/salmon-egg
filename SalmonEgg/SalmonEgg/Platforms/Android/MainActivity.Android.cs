using System;
using System.Threading;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

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
    private static TaskCompletionSource<bool>? _notificationPermissionSource;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

        base.OnCreate(savedInstanceState);
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
