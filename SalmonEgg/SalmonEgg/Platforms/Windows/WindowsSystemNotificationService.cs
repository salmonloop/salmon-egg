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
    private bool _isSubscribed;
    private bool _isListening;

    // A click can launch the process, so the activation can arrive before anything is listening. The
    // latest one is parked until Start, then replayed.
    private SystemNotificationActivatedEventArgs? _pendingActivation;

    public event EventHandler<SystemNotificationActivatedEventArgs>? Activated;

    /// <summary>
    /// Subscribes to notification activations and registers with the platform.
    /// </summary>
    /// <remarks>
    /// The Windows App SDK requires Register to be called before
    /// <c>AppInstance.GetActivatedEventArgs</c>, so the application entry point calls this before it
    /// reads its activation arguments. Activations are parked rather than raised until
    /// <see cref="Start"/> runs, because the shared listener does not exist yet at that point.
    /// </remarks>
    public void EnsureActivationListenerRegistered()
    {
        lock (_sync)
        {
            if (_isSubscribed)
            {
                return;
            }

            _isSubscribed = true;
        }

        try
        {
            // Documented order: subscribe to NotificationInvoked, then Register. Registering first
            // would let a launch-time activation land before there is a handler for it.
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            EnsureRegistered();
        }
        catch
        {
            // A host without the notification stack cannot report activations. Posting already
            // reports Unsupported, so there is nothing further to surface here.
            lock (_sync)
            {
                _isSubscribed = false;
            }
        }
    }

    public void Start()
    {
        EnsureActivationListenerRegistered();

        lock (_sync)
        {
            if (_isListening)
            {
                return;
            }

            _isListening = true;
        }

        DrainPendingActivation();
    }

    /// <summary>
    /// Records a launch-time notification activation so it survives until a listener exists.
    /// </summary>
    /// <remarks>
    /// Windows delivers a cold-start notification click through COM activation, which the app observes
    /// on its own launch path — before the shell has built the conversation catalog and before
    /// <see cref="Start"/> runs. The application entry point hands it here so it is not lost.
    /// </remarks>
    public void CaptureLaunchActivation(AppNotificationActivatedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!TryReadActivation(args, out var activation))
        {
            return;
        }

        ParkOrRaise(activation);
    }

    private void DrainPendingActivation()
    {
        SystemNotificationActivatedEventArgs? pending;
        lock (_sync)
        {
            pending = _pendingActivation;
            _pendingActivation = null;
        }

        if (pending is not null)
        {
            Activated?.Invoke(this, pending);
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (TryReadActivation(args, out var activation))
        {
            ParkOrRaise(activation);
        }
    }

    // Registering happens before the shared listener is attached, so an activation in that window has
    // to be parked. The newest one wins: it is the click the user just made.
    private void ParkOrRaise(SystemNotificationActivatedEventArgs activation)
    {
        lock (_sync)
        {
            if (!_isListening)
            {
                _pendingActivation = activation;
                return;
            }
        }

        Activated?.Invoke(this, activation);
    }

    private static bool TryReadActivation(
        AppNotificationActivatedEventArgs args,
        out SystemNotificationActivatedEventArgs activation)
    {
        args.Arguments.TryGetValue(NotificationIdArgument, out var notificationId);
        args.Arguments.TryGetValue(ConversationIdArgument, out var conversationId);
        if (string.IsNullOrWhiteSpace(notificationId))
        {
            activation = null!;
            return false;
        }

        activation = new SystemNotificationActivatedEventArgs(notificationId, conversationId);
        return true;
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
