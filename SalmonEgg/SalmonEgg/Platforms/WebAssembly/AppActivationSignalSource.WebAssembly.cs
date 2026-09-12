#if __WASM__
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SalmonEgg.Platforms.WebAssembly;

namespace SalmonEgg.Presentation.Services;

public sealed partial class AppActivationSignalSource
{
    private CancellationTokenSource? _browserActivityLifetime;
    private JSObject? _browserActivitySubscription;
    private Window? _browserActivityWindow;
    private bool _isBrowserDocumentActive;

    partial void AttachPlatformActivity(Window window)
    {
        if (_browserActivityWindow is not null) return;
        _browserActivityWindow = window;
        _browserActivityLifetime = new CancellationTokenSource();
        _isBrowserDocumentActive = false;
        _ = ObserveBrowserActivityAsync(window, _browserActivityLifetime.Token);
    }

    partial void DetachPlatformActivity(Window window)
    {
        if (!OperatingSystem.IsBrowser()) return;
        if (!ReferenceEquals(_browserActivityWindow, window)) return;
        _browserActivityWindow = null;
        _isBrowserDocumentActive = false;
        _browserActivityLifetime?.Cancel();
        _browserActivityLifetime?.Dispose();
        _browserActivityLifetime = null;
        if (_browserActivitySubscription is not { } subscription) return;
        _browserActivitySubscription = null;
        try
        {
            StopObservingDocumentActivityInterop(subscription);
        }
        catch (Exception)
        {
#if DEBUG
            _logger.LogDebug("Could not detach browser document activity observer.");
#endif
        }
        finally
        {
            subscription.Dispose();
        }
    }

    partial void ConstrainPlatformActivity(ref bool isActive) => isActive &= _isBrowserDocumentActive;

    private async Task ObserveBrowserActivityAsync(Window window, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsBrowser()) return;
        try
        {
            await WasmPlatformShellService.EnsureShellModuleImportedAsync(cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(_browserActivityWindow, window)) return;

            // Uno 6.7.103 wires visibility for its DOM renderer, while this Skia browser host does
            // not forward it to Window.Activated (WebAssemblyWindowWrapper.ts at cd452e3e). Remove
            // this bridge once that renderer forwards visibility/focus and the browser gate passes.
            _browserActivitySubscription = ObserveDocumentActivityInterop(
                active => ApplyBrowserActivity(window, cancellationToken, active));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!cancellationToken.IsCancellationRequested && ReferenceEquals(_browserActivityWindow, window))
            {
                _isBrowserDocumentActive = false;
            }
#if DEBUG
            _logger.LogDebug("Could not observe browser document activity.");
#endif
        }
    }

    private void ApplyBrowserActivity(Window window, CancellationToken cancellationToken, bool active)
    {
        void Apply()
        {
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(_browserActivityWindow, window)) return;
            _isBrowserDocumentActive = active;
            PublishActivity();
        }

        if (window.DispatcherQueue.HasThreadAccess) Apply();
        else window.DispatcherQueue.TryEnqueue(Apply);
    }

    [JSImport("observeDocumentActivity", "salmon-egg-wasm-shell.js")]
    private static partial JSObject ObserveDocumentActivityInterop(
        [JSMarshalAs<JSType.Function<JSType.Boolean>>] Action<bool> sink);

    [JSImport("stopObservingDocumentActivity", "salmon-egg-wasm-shell.js")]
    private static partial void StopObservingDocumentActivityInterop(JSObject subscription);
}
#endif
