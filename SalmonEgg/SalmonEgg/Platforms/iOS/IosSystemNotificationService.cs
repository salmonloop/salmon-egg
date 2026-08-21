#if __IOS__
using System;
using System.Threading;
using System.Threading.Tasks;
using Foundation;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using UserNotifications;

namespace SalmonEgg.Platforms.iOS;

public sealed class IosSystemNotificationService : ISystemNotificationService, ISystemNotificationActivationSource
{
    private const string ConversationIdUserInfoKey = "conversationId";

    private readonly object _sync = new();
    private Task<bool>? _authorizationTask;
    private ActivationDelegate? _activationDelegate;

    public event EventHandler<SystemNotificationActivatedEventArgs>? Activated;

    public void Start()
    {
        lock (_sync)
        {
            if (_activationDelegate is not null)
            {
                return;
            }

            // The delegate must be set before the system delivers a launch-time response, and it is
            // the only way UNUserNotificationCenter reports taps. Held in a field so it is not
            // collected while the system still holds the native reference.
            _activationDelegate = new ActivationDelegate(this);
        }

        UNUserNotificationCenter.Current.Delegate = _activationDelegate;
    }

    private void RaiseActivated(UNNotificationResponse response)
    {
        var request = response.Notification?.Request;
        var notificationId = request?.Identifier;
        if (string.IsNullOrWhiteSpace(notificationId))
        {
            return;
        }

        var conversationId = request?.Content?.UserInfo?
            .ObjectForKey(new NSString(ConversationIdUserInfoKey)) as NSString;
        Activated?.Invoke(
            this,
            new SystemNotificationActivatedEventArgs(notificationId, conversationId?.ToString()));
    }

    private sealed class ActivationDelegate : UNUserNotificationCenterDelegate
    {
        private readonly IosSystemNotificationService _owner;

        public ActivationDelegate(IosSystemNotificationService owner)
        {
            _owner = owner;
        }

        public override void DidReceiveNotificationResponse(
            UNUserNotificationCenter center,
            UNNotificationResponse response,
            Action completionHandler)
        {
            try
            {
                _owner.RaiseActivated(response);
            }
            finally
            {
                // The system requires this call; skipping it stalls further delivery.
                completionHandler();
            }
        }
    }

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
            if (!string.IsNullOrWhiteSpace(request.ConversationId))
            {
                // UserInfo comes back on the tap response, which is how a tap routes.
                content.UserInfo = NSDictionary.FromObjectAndKey(
                    new NSString(request.ConversationId.Trim()),
                    new NSString(ConversationIdUserInfoKey));
            }

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
