using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using SalmonEgg.Platforms.Windows;
using SalmonEgg.Presentation.Core.Services.Input;
using Microsoft.Extensions.DependencyInjection;
using WinUIKeyEventArgs = Microsoft.UI.Input.KeyEventArgs;

namespace SalmonEgg;

public sealed partial class MainPage
{
    private TrayIconManager? _trayIcon;
    private InputKeyboardSource? _debugKeyboardSource;
    private IGamepadNavigationDispatcher? _virtualGamepadNavigationDispatcher;
    private bool _allowClose;

    partial void InitializeTray()
    {
        UpdateTrayState();

        var window = App.MainWindowInstance;
        if (window?.AppWindow != null)
        {
            window.AppWindow.Closing -= OnAppWindowClosing;
            window.AppWindow.Closing += OnAppWindowClosing;
        }
    }

    partial void UpdateTrayState()
    {
        if (!Preferences.IsMinimizeToTraySupported)
        {
            DisposePlatformTray();
            return;
        }

        if (!Preferences.MinimizeToTray)
        {
            DisposePlatformTray();
            ShowMainWindow();
            return;
        }

        EnsureTrayIcon();
    }

    partial void DisposePlatformTray()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon != null)
        {
            return;
        }

        var window = App.MainWindowInstance;
        if (window == null)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _trayIcon = new TrayIconManager(hwnd, "Salmon Egg", ShowMainWindow, ExitFromTray);
    }

    private void ShowMainWindow()
    {
        var window = App.MainWindowInstance;
        if (window == null)
        {
            return;
        }

        try
        {
            window.AppWindow?.Show();
        }
        catch
        {
        }
    }

    private void ExitFromTray()
    {
        _allowClose = true;
        DisposePlatformTray();
        App.MainWindowInstance?.Close();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        if (!Preferences.MinimizeToTray)
        {
            return;
        }

        args.Cancel = true;
        sender.Hide();
    }

    partial void AttachDebugKeyLogging()
    {
#if DEBUG
        if (XamlRoot?.ContentIsland is null)
        {
            App.BootLog("MainPage KeyDown attach skipped: ContentIsland unavailable");
            _ = DispatcherQueue.TryEnqueue(AttachDebugKeyLogging);
            return;
        }

        _debugKeyboardSource ??= InputKeyboardSource.GetForIsland(XamlRoot.ContentIsland);
        _debugKeyboardSource.KeyDown -= OnDebugKeyDown;
        _debugKeyboardSource.KeyDown += OnDebugKeyDown;
        App.BootLog("MainPage KeyDown attach succeeded");
#endif
    }

    partial void DetachDebugKeyLogging()
    {
#if DEBUG
        if (_debugKeyboardSource is null)
        {
            return;
        }

        _debugKeyboardSource.KeyDown -= OnDebugKeyDown;
        App.BootLog("MainPage KeyDown detached");
#endif
    }

#if DEBUG
    private static void OnDebugKeyDown(InputKeyboardSource sender, WinUIKeyEventArgs args)
    {
        App.BootLog($"MainPage KeyDown: key={args.VirtualKey} handled={args.Handled}");
    }
#endif

    partial void AttachPlatformGamepadDirectionalBridge()
    {
        if (XamlRoot?.ContentIsland is null)
        {
            _ = DispatcherQueue.TryEnqueue(AttachPlatformGamepadDirectionalBridge);
            return;
        }

        _debugKeyboardSource ??= InputKeyboardSource.GetForIsland(XamlRoot.ContentIsland);
        _virtualGamepadNavigationDispatcher ??= App.ServiceProvider.GetRequiredService<IGamepadNavigationDispatcher>();
        _debugKeyboardSource.KeyDown -= OnPlatformGamepadDirectionalBridgeKeyDown;
        _debugKeyboardSource.KeyDown += OnPlatformGamepadDirectionalBridgeKeyDown;
    }

    partial void DetachPlatformGamepadDirectionalBridge()
    {
        if (_debugKeyboardSource is null)
        {
            return;
        }

        _debugKeyboardSource.KeyDown -= OnPlatformGamepadDirectionalBridgeKeyDown;
    }

    private void OnPlatformGamepadDirectionalBridgeKeyDown(InputKeyboardSource sender, WinUIKeyEventArgs args)
    {
        switch (args.VirtualKey)
        {
            case Windows.System.VirtualKey.GamepadDPadRight:
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (IsFocusWithinMainNavigation())
                    {
                        _ = TryMoveFocusFromMainNavigationIntoCurrentContent();
                    }
                });
                break;
            case Windows.System.VirtualKey.GamepadB:
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    _ = _virtualGamepadNavigationDispatcher?.TryDispatchWithoutNativeFallback(GamepadNavigationIntent.Back);
                });
                break;
        }
    }
}
