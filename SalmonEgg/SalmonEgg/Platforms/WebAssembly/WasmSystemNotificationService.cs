#if __WASM__
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Platforms.WebAssembly;

/// <summary>
/// Shows notifications through the browser Notification API.
/// </summary>
/// <remarks>
/// Browsers only grant notification permission from a user gesture, which is why the settings toggle
/// is the thing that asks. A browser without the API, or one that refuses a page-created
/// notification, is reported as an absent capability rather than a failure.
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed partial class WasmSystemNotificationService : ISystemNotificationService
{
    private const string NotificationsModuleName = "salmon-egg-wasm-notifications.js";

    private static readonly SemaphoreSlim ModuleLock = new(1, 1);
    private static JSObject? _notificationsModule;

    // Browser permission strings, plus the sentinel the module returns when the API is absent.
    private const string PermissionGranted = "granted";
    private const string PermissionDenied = "denied";
    private const string PermissionUnsupported = "unsupported";

    // IsSupported is a synchronous property that the settings page binds to, so it cannot await the
    // module import. Reading `globalThis.Notification` needs no module, which keeps this honest without
    // forcing an async capability check into the binding path.
    public bool IsSupported
    {
        get
        {
            try
            {
                using var notification = JSHost.GlobalThis.GetPropertyAsJSObject("Notification");
                return notification is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<SystemNotificationPermissionResult> RequestPermissionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await EnsureModuleImportedAsync(cancellationToken).ConfigureAwait(false);
            var permission = await RequestPermissionInteropAsync().ConfigureAwait(false);
            return permission switch
            {
                PermissionGranted => SystemNotificationPermissionResult.Granted,
                PermissionUnsupported => SystemNotificationPermissionResult.Unsupported,
                // "default" means the prompt was dismissed without a decision, which is not consent.
                _ => SystemNotificationPermissionResult.Denied
            };
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
            await EnsureModuleImportedAsync(cancellationToken).ConfigureAwait(false);
            var permission = GetPermissionInterop();
            if (permission == PermissionUnsupported)
            {
                return SystemNotificationResult.Unsupported;
            }

            if (permission != PermissionGranted)
            {
                return SystemNotificationResult.Denied;
            }

            // The notification id is passed through as the browser tag, so one turn maps to one
            // notification rather than stacking a duplicate.
            return ShowNotificationInterop(
                request.NotificationId,
                request.Title.Trim(),
                request.Body.Trim())
                ? SystemNotificationResult.Shown
                : SystemNotificationResult.Failed;
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

    private static async Task EnsureModuleImportedAsync(CancellationToken cancellationToken)
    {
        if (_notificationsModule != null)
        {
            return;
        }

        await ModuleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_notificationsModule != null)
            {
                return;
            }

            _notificationsModule = await JSHost.ImportAsync(
                NotificationsModuleName,
                WasmModuleUrlResolver.Resolve(NotificationsModuleName),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ModuleLock.Release();
        }
    }

    [JSImport("getPermission", NotificationsModuleName)]
    internal static partial string GetPermissionInterop();

    [JSImport("requestPermission", NotificationsModuleName)]
    internal static partial Task<string> RequestPermissionInteropAsync();

    [JSImport("showNotification", NotificationsModuleName)]
    internal static partial bool ShowNotificationInterop(string notificationId, string title, string body);
}
#endif
