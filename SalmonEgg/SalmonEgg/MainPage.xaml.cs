using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
#if WINDOWS
using Microsoft.UI;
using Microsoft.UI.Windowing;
#endif
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using SalmonEgg.Application.Common.Shell;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;
using SalmonEgg.Presentation.Views.Chat;
using SalmonEgg.Presentation.Views.Start;

namespace SalmonEgg;

public sealed partial class MainPage : Page
{
    private const double DefaultCompactPaneLength = 72;
    private const double DefaultOpenPaneLength = 240;
    private const double RightPanelMinWidth = 240;
    private const double RightPanelMaxWidth = 520;
    private bool _isResizingRightPanel;
    private double _rightPanelResizeStartX;
    private double _rightPanelResizeStartWidth;
    private string? _activeRightPanel;
    private string _activePrimaryNavKey = MainNavItemKeys.Start;
    private bool _suppressNavSelectionChanged;
    private long _navSelectionRequestId;
    private readonly ShellPanePolicy _panePolicy = new();
#if WINDOWS
    private AppWindowTitleBar? _appWindowTitleBar;
#endif

    public AppPreferencesViewModel Preferences { get; }
    public MainNavigationViewModel NavVM { get; }
    private readonly ChatViewModel _chatViewModel;

    public MainPage()
    {
        App.BootLog("MainPage: ctor start");
        // 1. 在初始化组件前获取 ViewModel，确保 x:Bind 绑定正常
        Preferences = App.ServiceProvider.GetRequiredService<AppPreferencesViewModel>();
        NavVM = App.ServiceProvider.GetRequiredService<MainNavigationViewModel>();
        _chatViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();

        this.InitializeComponent();
        App.BootLog("MainPage: InitializeComponent done");

        Loaded += OnMainPageLoaded;
        Unloaded += OnMainPageUnloaded;
        ContentFrame.Navigated += OnContentFrameNavigated;

#if !WINDOWS
        // Cross-platform fallback "Mica-like" backdrop.
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AppBackdropBrush", out var brush) && brush is Brush b)
        {
            Background = b;
        }
#endif

        // 2. 监听全局设置变化（如动画开关、主题、背景材质）
        Preferences.PropertyChanged += OnPreferencesPropertyChanged;

        // 3. 初始化主题与动画状态
        ApplyTheme();
        ApplyBackdrop();
        UpdateNavigationTransitions();
        App.BootLog("MainPage: transitions updated");

        ConfigureNavigationView();

        // 4. 启动后默认进入开始界面
        NavVM.SelectStart();
        NavigateToStart();
        App.BootLog("MainPage: navigated to StartView");
    }

    private void OnMainPageUnloaded(object sender, RoutedEventArgs e)
    {
        Preferences.PropertyChanged -= OnPreferencesPropertyChanged;
    }

    private void ConfigureNavigationView()
    {
        if (MainNavView == null)
        {
            return;
        }
    }

    private void SetSelectedSettingsItemDeferred()
    {
        if (MainNavView?.SettingsItem is null)
        {
            return;
        }

        if (ReferenceEquals(MainNavView.SelectedItem, MainNavView.SettingsItem))
        {
            return;
        }

        var requestId = Interlocked.Increment(ref _navSelectionRequestId);
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (MainNavView?.SettingsItem is null)
            {
                return;
            }

            if (requestId != _navSelectionRequestId)
            {
                return;
            }

            if (ReferenceEquals(MainNavView.SelectedItem, MainNavView.SettingsItem))
            {
                return;
            }

            _suppressNavSelectionChanged = true;
            try
            {
                MainNavView.SelectedItem = MainNavView.SettingsItem;
            }
            finally
            {
                _suppressNavSelectionChanged = false;
            }
        });
    }

    public void NavigateToChat()
    {
        EnsureChatContent();
        if (!string.IsNullOrWhiteSpace(_chatViewModel.CurrentSessionId))
        {
            NavVM.SelectSession(_chatViewModel.CurrentSessionId!);
        }
    }

    public void NavigateToStart()
    {
        EnsureStartContent();
        NavVM.SelectStart();
    }

    public void NavigateToSettingsSubPage(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            key = "General";
        }

        EnsureSettingsContent(key);
        SetSelectedSettingsItemDeferred();
    }

    private static Type GetSettingsShellPageType() => typeof(SalmonEgg.Presentation.Views.SettingsShellPage);

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.IsAnimationEnabled))
        {
            UpdateNavigationTransitions();
        }

        if (e.PropertyName == nameof(Preferences.Theme))
        {
            ApplyTheme();
        }

        if (e.PropertyName == nameof(Preferences.Backdrop))
        {
            ApplyBackdrop();
        }
    }

    private void ApplyTheme()
    {
        var theme = Preferences.Theme?.Trim();
        var requested = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (App.MainWindowInstance?.Content is FrameworkElement root && root.RequestedTheme != requested)
        {
            root.RequestedTheme = requested;
        }
    }

    private void ApplyBackdrop()
    {
#if WINDOWS
        try
        {
            var window = App.MainWindowInstance;
            if (window == null)
            {
                return;
            }

            var pref = (Preferences.Backdrop ?? "System").Trim();
            Microsoft.UI.Xaml.Media.SystemBackdrop? backdrop = pref switch
            {
                "Mica" => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                    ? new Microsoft.UI.Xaml.Media.MicaBackdrop()
                    : OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
                        ? new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop()
                        : null,
                "Acrylic" => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
                    ? new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop()
                    : null,
                "Solid" => null,
                _ => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                    ? new Microsoft.UI.Xaml.Media.MicaBackdrop()
                    : OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
                        ? new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop()
                        : null
            };

            window.SystemBackdrop = backdrop;
        }
        catch
        {
        }
#endif
    }

    private void UpdateNavigationTransitions()
    {
        // 根据全局设置动态开启或关闭 Frame 的过渡动画
        if (Preferences.IsAnimationEnabled)
        {
            ContentFrame.ContentTransitions = new TransitionCollection
            {
#if WINDOWS
                new NavigationThemeTransition { DefaultNavigationTransitionInfo = new EntranceNavigationTransitionInfo() }
#else
                new EntranceThemeTransition()
#endif
            };
        }
        else
        {
            ContentFrame.ContentTransitions = null;
        }
    }

    private void NavigateTo(Type pageType)
    {
#if WINDOWS
        var transition = Preferences.IsAnimationEnabled
            ? (NavigationTransitionInfo)new EntranceNavigationTransitionInfo()
            : new SuppressNavigationTransitionInfo();
        ContentFrame.Navigate(pageType, null, transition);
#else
        ContentFrame.Navigate(pageType);
#endif
    }

    private void NavigateTo(Type pageType, object? parameter)
    {
#if WINDOWS
        var transition = Preferences.IsAnimationEnabled
            ? (NavigationTransitionInfo)new EntranceNavigationTransitionInfo()
            : new SuppressNavigationTransitionInfo();
        ContentFrame.Navigate(pageType, parameter, transition);
#else
        ContentFrame.Navigate(pageType, parameter);
#endif
    }

    private void OnMainNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavSelectionChanged)
        {
            return;
        }

        if (args.IsSettingsSelected)
        {
            NavigateToSettingsSubPage("General");
            return;
        }

        if (args.SelectedItem is StartNavItemViewModel)
        {
            EnsureStartContent();
            return;
        }

        if (args.SelectedItem is SessionNavItemViewModel session && !session.IsPlaceholder)
        {
            EnsureChatContent();

            var projectId = session.ProjectId;
            Preferences.LastSelectedProjectId = string.Equals(projectId, MainNavigationViewModel.UnclassifiedProjectId, StringComparison.Ordinal)
                ? null
                : projectId;

            _ = _chatViewModel.TrySwitchToSessionAsync(session.SessionId);
            return;
        }
    }

    private void OnMainNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        var invoked = args.InvokedItemContainer?.DataContext ?? args.InvokedItem;
        if (invoked is ProjectNavItemViewModel project)
        {
            NavVM.ToggleProjectExpanded(project.ProjectId);
            return;
        }

        if (invoked is MoreSessionsNavItemViewModel more)
        {
            _ = more.ShowMoreCommand.ExecuteAsync(null);
            return;
        }
    }

    private void EnsureChatContent()
    {
        _activePrimaryNavKey = MainNavItemKeys.Chat;

        if (ContentFrame?.CurrentSourcePageType != typeof(ChatView))
        {
            NavigateTo(typeof(ChatView));
        }

        UpdateRightPanelAvailability(true);
        UpdateBackButtonState();
    }

    private void EnsureStartContent()
    {
        _activePrimaryNavKey = MainNavItemKeys.Start;

        if (ContentFrame?.CurrentSourcePageType != typeof(StartView))
        {
            NavigateTo(typeof(StartView));
        }

        UpdateRightPanelAvailability(false);
        UpdateBackButtonState();
    }

    private void EnsureSettingsContent(string key)
    {
        _activePrimaryNavKey = MainNavItemKeys.Settings;

        var pageType = GetSettingsShellPageType();
        if (ContentFrame?.CurrentSourcePageType != pageType)
        {
            NavigateTo(pageType, key);
        }
        else
        {
            // SettingsShellPage is already loaded; ask it to switch section without resetting the shell.
            (ContentFrame.Content as SalmonEgg.Presentation.Views.SettingsShellPage)?.NavigateToSection(key);
        }

        UpdateRightPanelAvailability(false);
        UpdateBackButtonState();
    }

    private void OnRightPanelButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || RightPanelColumn is null)
        {
            return;
        }

        var key = button.Tag?.ToString() ?? string.Empty;
        if (RightPanelColumn.Visibility == Visibility.Visible && _activeRightPanel == key)
        {
            CloseRightPanel();
            return;
        }

        OpenRightPanel(key);
    }

    private void OpenRightPanel(string key)
    {
        if (RightPanelColumn is null || RightPanelTitle is null)
        {
            return;
        }

        _activeRightPanel = key;
        RightPanelColumn.Visibility = Visibility.Visible;
        var baseWidth = double.IsNaN(RightPanelColumn.Width) || RightPanelColumn.Width <= 0 ? 320 : RightPanelColumn.Width;
        RightPanelColumn.Width = Math.Clamp(baseWidth, RightPanelMinWidth, RightPanelMaxWidth);

        RightPanelTitle.Text = key switch
        {
            "Diff" => "Diff",
            "Todo" => "Todo",
            _ => "Panel"
        };

        DiffPanel.Visibility = key == "Diff" ? Visibility.Visible : Visibility.Collapsed;
        TodoPanel.Visibility = key == "Todo" ? Visibility.Visible : Visibility.Collapsed;

        DiffPanelButton.IsChecked = key == "Diff";
        TodoPanelButton.IsChecked = key == "Todo";
    }

    private void CloseRightPanel()
    {
        if (RightPanelColumn is null)
        {
            return;
        }

        _activeRightPanel = null;
        RightPanelColumn.Visibility = Visibility.Collapsed;

        DiffPanel.Visibility = Visibility.Collapsed;
        TodoPanel.Visibility = Visibility.Collapsed;

        DiffPanelButton.IsChecked = false;
        TodoPanelButton.IsChecked = false;
    }

    private void UpdateRightPanelAvailability(bool isChat)
    {
        DiffPanelButton.IsEnabled = isChat;
        TodoPanelButton.IsEnabled = isChat;

        if (!isChat)
        {
            CloseRightPanel();
        }
    }

    private void OnRightPanelResizerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (RightPanelColumn is null || RightPanelResizer is null || RightPanelColumn.Visibility != Visibility.Visible)
        {
            return;
        }

        _isResizingRightPanel = true;
        _rightPanelResizeStartX = e.GetCurrentPoint(this).Position.X;
        _rightPanelResizeStartWidth = RightPanelColumn.Width;

        RightPanelResizer.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnRightPanelResizerPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizingRightPanel || RightPanelColumn is null)
        {
            return;
        }

        var currentX = e.GetCurrentPoint(this).Position.X;
        var delta = currentX - _rightPanelResizeStartX;
        var newWidth = _rightPanelResizeStartWidth - delta;

        if (newWidth < RightPanelMinWidth)
        {
            newWidth = RightPanelMinWidth;
        }
        else if (newWidth > RightPanelMaxWidth)
        {
            newWidth = RightPanelMaxWidth;
        }

        RightPanelColumn.Width = newWidth;
        e.Handled = true;
    }

    private void OnRightPanelResizerPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndRightPanelResize(e.Pointer);
        e.Handled = true;
    }

    private void OnRightPanelResizerPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndRightPanelResize(e.Pointer);
    }

    private void EndRightPanelResize(Pointer pointer)
    {
        if (!_isResizingRightPanel || RightPanelResizer is null)
        {
            return;
        }

        _isResizingRightPanel = false;
        RightPanelResizer.ReleasePointerCapture(pointer);
    }

    private void OnMainPageLoaded(object sender, RoutedEventArgs e)
    {
        ConfigureTitleBar();
        UpdateNavPaneToggleUi();
        NavVM.RebuildTree();
    }

    private void OnContentFrameNavigated(object sender, NavigationEventArgs e)
    {
        UpdateBackButtonState();
        SyncNavSelectionFromCurrentPage(e.SourcePageType);
    }

    private void OnTitleBarBackClick(object sender, RoutedEventArgs e)
    {
        if (ContentFrame?.CanGoBack == true)
        {
            ContentFrame.GoBack();
        }
    }

    private void OnToggleLeftNavClick(object sender, RoutedEventArgs e)
    {
        ToggleNavPane();
    }

    private void ToggleNavPane()
    {
        if (MainNavView == null)
        {
            return;
        }

        MainNavView.CompactPaneLength = DefaultCompactPaneLength;
        MainNavView.OpenPaneLength = DefaultOpenPaneLength;
        MainNavView.IsPaneOpen = _panePolicy.Toggle(MainNavView.IsPaneOpen);
        UpdateNavPaneToggleUi();
    }

    private void UpdateNavPaneToggleUi()
    {
        if (MainNavView == null || TitleBarToggleLeftNavButton == null)
        {
            return;
        }

        ToolTipService.SetToolTip(TitleBarToggleLeftNavButton, MainNavView.IsPaneOpen ? "折叠左侧边栏" : "展开左侧边栏");
    }

    private void UpdateBackButtonState()
    {
        if (TitleBarBackButton == null || ContentFrame == null)
        {
            return;
        }

        // Better UX than a permanently-disabled button in the title bar.
        TitleBarBackButton.Visibility = ContentFrame.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        TitleBarBackButton.IsEnabled = ContentFrame.CanGoBack;
    }

    private void SyncNavSelectionFromCurrentPage(Type? pageType)
    {
        if (pageType == null || MainNavView == null)
        {
            return;
        }

        if (pageType == typeof(ChatView))
        {
            _activePrimaryNavKey = MainNavItemKeys.Chat;
            if (!string.IsNullOrWhiteSpace(_chatViewModel.CurrentSessionId))
            {
                NavVM.SelectSession(_chatViewModel.CurrentSessionId!);
            }
            return;
        }

        if (pageType == typeof(StartView))
        {
            _activePrimaryNavKey = MainNavItemKeys.Start;
            NavVM.SelectStart();
            return;
        }

        if (pageType == typeof(SalmonEgg.Presentation.Views.SettingsShellPage))
        {
            _activePrimaryNavKey = MainNavItemKeys.Settings;
            SetSelectedSettingsItemDeferred();
        }
    }

    private void OnMainNavPaneOpened(NavigationView sender, object args)
    {
        UpdateNavPaneToggleUi();
    }

    private void OnMainNavPaneClosed(NavigationView sender, object args)
    {
        UpdateNavPaneToggleUi();
    }

    private void OnMainNavPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
    {
        var isMinimal = sender.DisplayMode == NavigationViewDisplayMode.Minimal;
        if (_panePolicy.ShouldCancelClosing(isMinimalMode: isMinimal))
        {
            args.Cancel = true;
        }
    }

    private void ConfigureTitleBar()
    {
        var window = App.MainWindowInstance;
        if (window == null || AppTitleBar is null || TitleBarDragRegion is null)
        {
            return;
        }

#if WINDOWS
        try
        {
            window.ExtendsContentIntoTitleBar = true;
            // Only the dedicated drag region participates in window dragging.
            window.SetTitleBar(TitleBarDragRegion);
        }
        catch
        {
            return;
        }

        if (TitleBarLeftButtons != null)
        {
            TitleBarLeftButtons.Visibility = Visibility.Visible;
        }

        if (TitleBarBackButton != null)
        {
            TitleBarBackButton.IsEnabled = ContentFrame.CanGoBack;
        }

        var appWindow = window.AppWindow;
        if (appWindow?.TitleBar == null)
        {
            return;
        }

        _appWindowTitleBar = appWindow.TitleBar;
        _appWindowTitleBar.ExtendsContentIntoTitleBar = true;
        _appWindowTitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        _appWindowTitleBar.BackgroundColor = Colors.Transparent;
        _appWindowTitleBar.InactiveBackgroundColor = Colors.Transparent;
        // Keep normal caption buttons transparent so they blend with our title bar,
        // but preserve system hover/pressed visuals (including the Close button red state).
        _appWindowTitleBar.ButtonBackgroundColor = Colors.Transparent;
        _appWindowTitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        UpdateTitleBarInsets();

        // Windows App SDK (current pinned version) does not expose LayoutMetricsChanged/IsVisibleChanged here.
        // We refresh insets on common window lifecycle events instead.
        window.Activated += OnMainWindowActivated;
        window.SizeChanged += OnMainWindowSizeChanged;
#endif
    }

#if WINDOWS
    private void OnMainWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        UpdateTitleBarInsets();
    }

    private void OnMainWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        UpdateTitleBarInsets();
    }

    private void UpdateTitleBarInsets()
    {
        if (_appWindowTitleBar == null || AppTitleBar is null || AppTitleBarContent is null)
        {
            return;
        }

        // Keep interactive content out of the system caption button area.
        AppTitleBarContent.Padding = new Thickness(_appWindowTitleBar.LeftInset, 0, _appWindowTitleBar.RightInset, 0);
        if (_appWindowTitleBar.Height > 0)
        {
            AppTitleBar.Height = _appWindowTitleBar.Height;
        }
    }
#endif
}
public sealed partial class MainPage
{
    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Preferences.PropertyChanged -= OnPreferencesPropertyChanged;

        Loaded -= OnMainPageLoaded;
        Unloaded -= OnMainPageUnloaded;
        ContentFrame.Navigated -= OnContentFrameNavigated;
#if WINDOWS
        if (App.MainWindowInstance != null)
        {
            App.MainWindowInstance.Activated -= OnMainWindowActivated;
            App.MainWindowInstance.SizeChanged -= OnMainWindowSizeChanged;
        }
#endif
    }
}
