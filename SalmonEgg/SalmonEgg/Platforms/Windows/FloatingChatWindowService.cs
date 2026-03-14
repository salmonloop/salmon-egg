using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Views.Chat;
using SalmonEgg.Presentation.Models;

namespace SalmonEgg.Platforms.Windows;

public sealed class FloatingChatWindowService : IFloatingChatWindowService
{
    private Microsoft.UI.Xaml.Window? _window;
    private AppWindow? _appWindow;
    private bool _isAlwaysOnTop = true;

    public event EventHandler<bool>? OpenStateChanged;
    public event EventHandler<bool>? AlwaysOnTopChanged;

    public bool IsOpen => _window != null;

    public bool IsAlwaysOnTop
    {
        get => _isAlwaysOnTop;
        set
        {
            if (_isAlwaysOnTop == value)
            {
                return;
            }

            _isAlwaysOnTop = value;
            ApplyAlwaysOnTop();
            AlwaysOnTopChanged?.Invoke(this, _isAlwaysOnTop);
        }
    }

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
        frame.Navigate(typeof(ChatView), new ChatViewHostOptions { IsFloatingHost = true });
        _window.Content = frame;

        InitializeAppWindow(_window);

        _window.Activate();
        OpenStateChanged?.Invoke(this, true);
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
                presenter.IsAlwaysOnTop = _isAlwaysOnTop;
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

    private void ApplyAlwaysOnTop()
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = _isAlwaysOnTop;
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
        OpenStateChanged?.Invoke(this, false);
    }
}
