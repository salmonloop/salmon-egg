#if HAS_UNO_SKIA || (!WINDOWS && !__ANDROID__ && !__IOS__ && !__WASM__)
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using Tmds.DBus.Protocol;

namespace SalmonEgg.Platforms.Desktop;

/// <summary>
/// Shows notifications through the freedesktop.org Desktop Notifications specification.
/// </summary>
/// <remarks>
/// Notifications are delivered by whichever desktop component owns
/// <c>org.freedesktop.Notifications</c> on the session bus. Headless sessions and minimal desktops
/// have no such owner, so this service reports Unsupported rather than pretending a notification
/// was shown.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxSystemNotificationService
    : ISystemNotificationService, ISystemNotificationActivationSource, IDisposable
{
    private const string NotificationsService = "org.freedesktop.Notifications";
    private const string NotificationsPath = "/org/freedesktop/Notifications";
    private const string NotificationsInterface = "org.freedesktop.Notifications";

    // "Expires according to the server's settings" — the desktop, not the app, owns the timeout.
    private const int ServerDefaultExpireTimeout = -1;

    // De-facto convention, NOT part of the specification: GNOME Shell and KDE treat an action keyed
    // "default" as the one a plain click on the notification body invokes, and do not render it as a
    // button. A server that follows the spec literally renders every action as a button, which is why
    // the paired label is localized rather than a placeholder.
    private const string DefaultActionKey = "default";

    // The bus only ever reports the numeric server id, so the maps below are what turn a click back
    // into the turn it was about. They are bounded because a long session completes many turns.
    private const int ActivationHistoryLimit = 256;

    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly object _sync = new();

    // The spec replaces a notification by the numeric id the server returned for it, so the stable
    // per-turn string id has to be mapped to whatever the server last handed back.
    private readonly Dictionary<string, uint> _serverIdsByNotificationId = new(StringComparer.Ordinal);

    // ActionInvoked and NotificationClosed only report the server id, so the reverse map is what turns
    // a click back into the turn it was about.
    private readonly Dictionary<uint, SystemNotificationActivatedEventArgs> _activationsByServerId = new();
    private readonly Queue<string> _notificationIdHistory = new();

    private bool _isListening;

    private DBusConnection? _connection;
    private bool _disposed;

    public LinuxSystemNotificationService(IStringLocalizer<CoreStrings> localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public event EventHandler<SystemNotificationActivatedEventArgs>? Activated;

    public void Start()
    {
        lock (_sync)
        {
            if (_isListening || _disposed)
            {
                return;
            }

            _isListening = true;
        }

        // Fire and forget: a desktop without a notification server simply never signals, and posting
        // already reports that as Unsupported.
        _ = WatchActionInvokedAsync();
    }

    private async Task WatchActionInvokedAsync()
    {
        try
        {
            var connection = await GetConnectionAsync(CancellationToken.None).ConfigureAwait(false);
            await connection.WatchSignalAsync(
                NotificationsService,
                NotificationsPath,
                NotificationsInterface,
                "ActionInvoked",
                static (Message message, object? _) => message.GetBodyReader().ReadUInt32(),
                OnActionInvoked,
                ObserverFlags.None,
                false,
                null).ConfigureAwait(false);
        }
        catch
        {
            lock (_sync)
            {
                _isListening = false;
            }
        }
    }

    // The signal body is (id, action_key). The key is not filtered: the only action this app registers
    // is the click-through one, and a server may report a plain body click under a key of its own
    // choosing, so any invocation on one of our notifications means the same intent.
    private void OnActionInvoked(Notification<uint> notification)
    {
        if (notification.Exception is not null || !notification.HasValue)
        {
            return;
        }

        SystemNotificationActivatedEventArgs? activation;
        lock (_sync)
        {
            _activationsByServerId.TryGetValue(notification.Value, out activation);
        }

        if (activation is not null)
        {
            Activated?.Invoke(this, activation);
        }
    }

    public bool IsSupported => OperatingSystem.IsLinux() && HasSessionBusAddress;

    private static bool HasSessionBusAddress
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"));

    public async Task<SystemNotificationPermissionResult> RequestPermissionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSupported)
        {
            return SystemNotificationPermissionResult.Unsupported;
        }

        // freedesktop notifications have no permission prompt. Reaching the notification server is
        // the only meaningful check, and it is the same check a later show would make.
        try
        {
            return await IsNotificationServerReachableAsync(cancellationToken).ConfigureAwait(false)
                ? SystemNotificationPermissionResult.Granted
                : SystemNotificationPermissionResult.Unsupported;
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

        if (!IsSupported)
        {
            return SystemNotificationResult.Unsupported;
        }

        try
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            var serverId = await NotifyAsync(connection, request, cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                RememberLocked(request, serverId);
            }

            return SystemNotificationResult.Shown;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DBusExceptionBase)
        {
            // No component owns org.freedesktop.Notifications on this session bus (headless session,
            // minimal desktop). That is an absent capability, not a failure to report to the user.
            return SystemNotificationResult.Unsupported;
        }
        catch
        {
            return SystemNotificationResult.Failed;
        }
    }

    private async Task<uint> NotifyAsync(
        DBusConnection connection,
        SystemNotificationRequest request,
        CancellationToken cancellationToken)
    {
        uint replacesId;
        lock (_sync)
        {
            _serverIdsByNotificationId.TryGetValue(request.NotificationId, out replacesId);
        }

        // CallMethodAsync takes ownership of the buffer, so it must not be disposed here. MessageWriter
        // is a ref struct and cannot live across an await, hence the separate builder.
        var message = BuildNotifyMessage(
            connection,
            request,
            replacesId,
            _localizer["SystemNotification_OpenAction"].Value);
        return await connection
            .CallMethodAsync(message, static (Message reply, object? _) => reply.GetBodyReader().ReadUInt32())
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static MessageBuffer BuildNotifyMessage(
        DBusConnection connection,
        SystemNotificationRequest request,
        uint replacesId,
        string openActionLabel)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: NotificationsService,
            path: NotificationsPath,
            @interface: NotificationsInterface,
            member: "Notify",
            signature: "susssasa{sv}i");

        writer.WriteString("Salmon Egg");
        writer.WriteUInt32(replacesId);
        writer.WriteString(string.Empty); // app_icon: the desktop entry's icon is the right default.
        writer.WriteString(request.Title.Trim());
        writer.WriteString(request.Body.Trim());
        // Even elements are action keys, odd elements are their user-visible labels.
        writer.WriteArray(new[] { DefaultActionKey, openActionLabel });

        var hints = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("category");
        writer.WriteVariantString("im.received");
        writer.WriteDictionaryEnd(hints);

        writer.WriteInt32(ServerDefaultExpireTimeout);
        return writer.CreateMessage();
    }

    private void RememberLocked(SystemNotificationRequest request, uint serverId)
    {
        if (!_serverIdsByNotificationId.ContainsKey(request.NotificationId))
        {
            _notificationIdHistory.Enqueue(request.NotificationId);
        }

        _serverIdsByNotificationId[request.NotificationId] = serverId;
        _activationsByServerId[serverId] = new SystemNotificationActivatedEventArgs(
            request.NotificationId,
            request.ConversationId);

        while (_notificationIdHistory.Count > ActivationHistoryLimit)
        {
            var evicted = _notificationIdHistory.Dequeue();
            if (_serverIdsByNotificationId.Remove(evicted, out var evictedServerId))
            {
                _activationsByServerId.Remove(evictedServerId);
            }
        }
    }

    private async Task<bool> IsNotificationServerReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            var message = BuildGetCapabilitiesMessage(connection);
            await connection
                .CallMethodAsync(message, static (Message reply, object? _) => reply.GetBodyReader().ReadArrayOfString())
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (DBusExceptionBase)
        {
            return false;
        }
    }

    private static MessageBuffer BuildGetCapabilitiesMessage(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: NotificationsService,
            path: NotificationsPath,
            @interface: NotificationsInterface,
            member: "GetCapabilities");
        return writer.CreateMessage();
    }

    private async Task<DBusConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        var existing = Volatile.Read(ref _connection);
        if (existing is not null)
        {
            return existing;
        }

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            existing = Volatile.Read(ref _connection);
            if (existing is not null)
            {
                return existing;
            }

            var connection = new DBusConnection(DBusAddress.Session
                ?? throw new InvalidOperationException("No D-Bus session bus address is available."));
            await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _connection, connection);
            return connection;
        }
        finally
        {
            _connectionGate.Release();
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

        Interlocked.Exchange(ref _connection, null)?.Dispose();
        _connectionGate.Dispose();
    }
}
#endif
