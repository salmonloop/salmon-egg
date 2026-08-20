#if __IOS__
using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using UserNotifications;

namespace SalmonEgg.Platforms.iOS;

public sealed class IosSystemNotificationService : ISystemNotificationService
{
    private readonly object _sync = new();
    private Task<bool>? _authorizationTask;

    public bool IsSupported => true;

    public async Task<SystemNotificationPermissionResult> RequestPermissionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var status = await GetAuthorizationStatusAsync(cancellationToken).ConfigureAwait(false);
            if (IsAuthorized(status))
            {
                return SystemNotificationPermissionResult.Granted;
            }

            if (status == UNAuthorizationStatus.Denied)
            {
                return SystemNotificationPermissionResult.Denied;
            }

            return await EnsureAuthorizationAsync(cancellationToken).ConfigureAwait(false)
                ? SystemNotificationPermissionResult.Granted
                : SystemNotificationPermissionResult.Denied;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return SystemNotificationPermissionResult.Failed;
        }
    }

    public async Task<SystemNotificationResult> ShowAsync(
        SystemNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.IsValid)
        {
            return SystemNotificationResult.Failed;
        }

        try
        {
            var authorizationStatus = await GetAuthorizationStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!IsAuthorized(authorizationStatus))
            {
                return SystemNotificationResult.Denied;
            }

            var content = new UNMutableNotificationContent
            {
                Title = request.Title.Trim(),
                Body = request.Body.Trim(),
                Sound = UNNotificationSound.Default
            };

            var trigger = UNTimeIntervalNotificationTrigger.Create(1, repeats: false);

            // The request identifier keys the notification, so re-notifying one turn replaces it.
            var notificationRequest = UNNotificationRequest.FromIdentifier(
                request.NotificationId,
                content,
                trigger);
            await AddNotificationRequestAsync(notificationRequest, cancellationToken).ConfigureAwait(false);
            return SystemNotificationResult.Shown;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return SystemNotificationResult.Failed;
        }
    }

    private Task<bool> EnsureAuthorizationAsync(CancellationToken cancellationToken)
    {
        Task<bool> authorizationTask;
        lock (_sync)
        {
            if (_authorizationTask is null || _authorizationTask.IsCanceled || _authorizationTask.IsFaulted)
            {
                _authorizationTask = RequestAuthorizationAsync();
            }

            authorizationTask = _authorizationTask;
        }

        return authorizationTask.WaitAsync(cancellationToken);
    }

    private static Task<bool> RequestAuthorizationAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        UNUserNotificationCenter.Current.RequestAuthorization(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound,
            (granted, error) => completion.TrySetResult(error is null && granted));
        return completion.Task;
    }

    private static async Task AddNotificationRequestAsync(
        UNNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        UNUserNotificationCenter.Current.AddNotificationRequest(
            request,
            error =>
            {
                if (error is null)
                {
                    completion.TrySetResult(null);
                }
                else
                {
                    completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
                }
            });
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<UNAuthorizationStatus> GetAuthorizationStatusAsync(
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<UNAuthorizationStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        UNUserNotificationCenter.Current.GetNotificationSettings(
            settings => completion.TrySetResult(settings.AuthorizationStatus));
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsAuthorized(UNAuthorizationStatus status)
        => status is UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional;
}
#endif
