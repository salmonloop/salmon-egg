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

    private static readonly object DelegateSync = new();
    private static ActivationDelegate? _activationDelegate;

    // A tap can launch the process, so the response can arrive before anything is listening. The
    // latest one is parked until a service instance starts, then replayed.
    private static SystemNotificationActivatedEventArgs? _pendingActivation;
    private static IosSystemNotificationService? _activationOwner;

    private readonly object _sync = new();
    private Task<bool>? _authorizationTask;

    public event EventHandler<SystemNotificationActivatedEventArgs>? Activated;

    /// <summary>
    /// Installs the notification delegate. Called from the application entry point, before the system
    /// can deliver a launch-time tap response.
    /// </summary>
    /// <remarks>
    /// UNUserNotificationCenter reports taps only through its delegate, and a response that arrives
    /// before one is assigned is not redelivered. The delegate therefore cannot wait for dependency
    /// injection or for the shell to mount; it parks the response until <see cref="Start"/> runs.
    /// </remarks>
    public static void InstallActivationDelegate()
    {
        lock (DelegateSync)
        {
            if (_activationDelegate is not null)
            {
                return;
            }

            // Held in a static field so it is not collected while the system holds the native reference.
            _activationDelegate = new ActivationDelegate();
        }

        UNUserNotificationCenter.Current.Delegate = _activationDelegate;
    }

    public void Start()
    {
        // The delegate is installed by the entry point; this only claims delivery and drains a tap
        // that landed before the shared layers existed.
        InstallActivationDelegate();

        SystemNotificationActivatedEventArgs? pending;
        lock (DelegateSync)
        {
            _activationOwner = this;
            pending = _pendingActivation;
            _pendingActivation = null;
        }

        if (pending is not null)
        {
            Activated?.Invoke(this, pending);
        }
    }

    private static void PublishActivation(UNNotificationResponse response)
    {
        var request = response.Notification?.Request;
        var notificationId = request?.Identifier;
        if (string.IsNullOrWhiteSpace(notificationId))
        {
            return;
        }

        var conversationId = request?.Content?.UserInfo?
            .ObjectForKey(new NSString(ConversationIdUserInfoKey)) as NSString;
        var activation = new SystemNotificationActivatedEventArgs(
            notificationId,
            conversationId?.ToString());

        IosSystemNotificationService? owner;
        lock (DelegateSync)
        {
            owner = _activationOwner;
            if (owner is null)
            {
                _pendingActivation = activation;
                return;
            }
        }

        owner.Activated?.Invoke(owner, activation);
    }

    private sealed class ActivationDelegate : UNUserNotificationCenterDelegate
    {
        public override void DidReceiveNotificationResponse(
            UNUserNotificationCenter center,
            UNNotificationResponse response,
            Action completionHandler)
        {
            try
            {
                PublishActivation(response);
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

            var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(1, false);

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
