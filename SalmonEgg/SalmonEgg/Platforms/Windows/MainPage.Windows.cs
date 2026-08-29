using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using SalmonEgg.Platforms.Windows;
using WinUIKeyEventArgs = Microsoft.UI.Input.KeyEventArgs;

namespace SalmonEgg;

public sealed partial class MainPage
{
    private TrayIconManager? _trayIcon;
#if DEBUG
    private InputKeyboardSource? _debugKeyboardSource;
#endif

    partial void InitializeTray()
    {
        // 只管托盘。窗口关闭路径已上提到共享的 MainPage.Shutdown.cs——Uno 的三个 Skia host
        // 同样 raise AppWindow.Closing 并尊重 Cancel，留在这里会让非 Windows 平台永远没有
        // teardown 时机（issue #126）。
        UpdateTrayState();
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

    partial void HideMainWindowToTray()
    {
        App.MainWindowInstance?.AppWindow?.Hide();
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
        catch (Exception ex)
        {
            // Restoring from tray is best-effort, but the failure must stay diagnosable.
            _logger.LogWarning(ex, "Failed to show main window from tray.");
        }
    }

    private void ExitFromTray()
    {
        DisposePlatformTray();
        // Tray exit is a second process boundary and must persist state like the window close path.
        // The shutdown workflow is idempotent, so both paths can drive it.
        _ = FlushRuntimeThenCloseAsync();
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

}
