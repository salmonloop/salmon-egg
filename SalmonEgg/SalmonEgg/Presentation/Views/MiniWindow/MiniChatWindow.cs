using System;
#if WINDOWS
using Microsoft.UI;
using Microsoft.UI.Windowing;
#endif
using Microsoft.UI.Xaml;

namespace SalmonEgg.Presentation.Views.MiniWindow;

public sealed class MiniChatWindow : Window
{
    private readonly MiniChatView _view;
    private readonly Presentation.Services.WindowBackdropService? _windowBackdropService;

#if WINDOWS
    private AppWindowTitleBar? _appWindowTitleBar;
#endif

    public MiniChatWindow()
    {
        _view = new MiniChatView();
        Content = _view;
        _windowBackdropService = App.ServiceProvider.GetService(typeof(Presentation.Services.WindowBackdropService)) as Presentation.Services.WindowBackdropService;
        _windowBackdropService?.Attach(this);

        Closed += OnWindowClosed;
        _view.Loaded += OnViewLoaded;
        _view.Unloaded += OnViewUnloaded;
    }

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        _windowBackdropService?.Detach(this);
        _view.Loaded -= OnViewLoaded;
        _view.Unloaded -= OnViewUnloaded;
        Closed -= OnWindowClosed;
    }

    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
#if WINDOWS
        ConfigureTitleBar();
#endif
    }

    private void OnViewUnloaded(object sender, RoutedEventArgs e)
    {
#if WINDOWS
        _appWindowTitleBar = null;
#endif
    }

#if WINDOWS
    private void ConfigureTitleBar()
    {
        if (_view.XamlRoot is null || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var titleBarElement = _view.EnsureNativeTitleBarElement();

        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(titleBarElement);
        }
        catch
        {
            return;
        }

        var appWindow = AppWindow;
        if (appWindow?.TitleBar == null)
        {
            return;
        }

        _appWindowTitleBar = appWindow.TitleBar;
        _appWindowTitleBar.ExtendsContentIntoTitleBar = true;
        _appWindowTitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        _appWindowTitleBar.BackgroundColor = Colors.Transparent;
        _appWindowTitleBar.InactiveBackgroundColor = Colors.Transparent;
        _appWindowTitleBar.ButtonBackgroundColor = Colors.Transparent;
        _appWindowTitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }
#endif
}
