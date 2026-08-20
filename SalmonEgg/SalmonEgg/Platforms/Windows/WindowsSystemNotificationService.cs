#if WINDOWS
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Platforms.Windows;

public sealed class WindowsSystemNotificationService : ISystemNotificationService
{
    private readonly object _sync = new();
    private bool _isRegistered;

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
            var notification = new AppNotificationBuilder()
                .AddText(request.Title.Trim())
                .AddText(request.Body.Trim())
                .BuildNotification();

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
