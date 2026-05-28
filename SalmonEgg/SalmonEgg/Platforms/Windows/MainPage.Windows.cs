using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using SalmonEgg.Platforms.Windows;
using SalmonEgg.Presentation.Core.Services.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using WinUIKeyEventArgs = Microsoft.UI.Input.KeyEventArgs;

namespace SalmonEgg;

public sealed partial class MainPage
{
    private static readonly TimeSpan PolledGamepadSuppressionWindow = TimeSpan.FromMilliseconds(150);

    private TrayIconManager? _trayIcon;
    private InputKeyboardSource? _debugKeyboardSource;
    private IGamepadNavigationDispatcher? _virtualGamepadNavigationDispatcher;
    private GamepadNavigationIntent? _lastNativeGamepadIntent;
    private long _lastNativeGamepadIntentTimestamp;
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
        RecordNativeGamepadIntent(args.VirtualKey);

        switch (args.VirtualKey)
        {
            case Windows.System.VirtualKey.GamepadDPadRight:
                if (IsFocusWithinMainNavigation() && TryMoveFocusFromMainNavigationIntoCurrentContent())
                {
                    args.Handled = true;
                }
                break;
            case Windows.System.VirtualKey.GamepadDPadUp:
                if ((_virtualGamepadNavigationDispatcher?.TryDispatchWithoutNativeFallback(GamepadNavigationIntent.MoveUp)).GetValueOrDefault())
                {
                    args.Handled = true;
                }
                break;
            case Windows.System.VirtualKey.GamepadDPadDown:
                if ((_virtualGamepadNavigationDispatcher?.TryDispatchWithoutNativeFallback(GamepadNavigationIntent.MoveDown)).GetValueOrDefault())
                {
                    args.Handled = true;
                }
                break;
            case Windows.System.VirtualKey.GamepadB:
                if ((_virtualGamepadNavigationDispatcher?.TryDispatchWithoutNativeFallback(GamepadNavigationIntent.Back)).GetValueOrDefault())
                {
                    args.Handled = true;
                }
                break;
        }
    }

    private bool ShouldSuppressPolledGamepadIntentForWindows(GamepadNavigationIntent intent)
    {
        if (_lastNativeGamepadIntent != intent || _lastNativeGamepadIntentTimestamp == 0)
        {
            return false;
        }

        var elapsed = Stopwatch.GetElapsedTime(_lastNativeGamepadIntentTimestamp);
        return elapsed <= PolledGamepadSuppressionWindow;
    }

    private void RecordNativeGamepadIntent(Windows.System.VirtualKey virtualKey)
    {
        var intent = virtualKey switch
        {
            Windows.System.VirtualKey.GamepadDPadUp => GamepadNavigationIntent.MoveUp,
            Windows.System.VirtualKey.GamepadDPadDown => GamepadNavigationIntent.MoveDown,
            Windows.System.VirtualKey.GamepadDPadLeft => GamepadNavigationIntent.MoveLeft,
            Windows.System.VirtualKey.GamepadDPadRight => GamepadNavigationIntent.MoveRight,
            Windows.System.VirtualKey.GamepadA => GamepadNavigationIntent.Activate,
            Windows.System.VirtualKey.GamepadB => GamepadNavigationIntent.Back,
            _ => (GamepadNavigationIntent?)null
        };

        if (intent is null)
        {
            return;
        }

        _lastNativeGamepadIntent = intent.Value;
        _lastNativeGamepadIntentTimestamp = Stopwatch.GetTimestamp();
    }
}
