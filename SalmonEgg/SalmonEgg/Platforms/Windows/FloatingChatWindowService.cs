using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Views.Chat;

namespace SalmonEgg.Platforms.Windows;

public sealed class FloatingChatWindowService : IFloatingChatWindowService
{
    private Microsoft.UI.Xaml.Window? _window;
    private AppWindow? _appWindow;

    public bool IsOpen => _window != null;

    public void Toggle()
    {
        if (IsOpen)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void Show()
    {
        if (_window != null)
        {
            _window.Activate();
            return;
        }

        _window = new Microsoft.UI.Xaml.Window();
        _window.Title = "对话悬小窗";
        _window.Closed += OnWindowClosed;

        var frame = new Frame();
        frame.Navigate(typeof(ChatView));
        _window.Content = frame;

        InitializeAppWindow(_window);

        _window.Activate();
    }

    public void Hide()
    {
        if (_window == null)
        {
            return;
        }

        _window.Close();
    }

    private void InitializeAppWindow(Microsoft.UI.Xaml.Window window)
    {
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = true;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = true;
            }

            _appWindow.Resize(new SizeInt32(420, 640));
        }
        catch
        {
            // Best-effort; if windowing APIs are unavailable, just show the window.
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_window != null)
        {
            _window.Closed -= OnWindowClosed;
        }
        _window = null;
        _appWindow = null;
    }
}
