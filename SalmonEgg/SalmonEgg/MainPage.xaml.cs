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
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;
using SalmonEgg.Presentation.Views.Chat;
using SalmonEgg.Presentation.Views.Start;
#if WINDOWS
using SalmonEgg.Platforms.Windows;
#endif

namespace SalmonEgg;

public sealed partial class MainPage : Page
{
    private const double DefaultCompactPaneLength = 72;
    private const double DefaultOpenPaneLength = 240;
    private const double NavPaneAnimationDurationMs = 180;
    private const double RightPanelMinWidth = 240;
    private const double RightPanelMaxWidth = 520;
    private const double RightPanelAnimationOffset = 16;
    private bool _isResizingRightPanel;
    private double _rightPanelResizeStartX;
    private double _rightPanelResizeStartWidth;
    private string? _activeRightPanel;
    private double _rightPanelLastWidth = 320;
    private Storyboard? _rightPanelStoryboard;
    private Storyboard? _navPaneStoryboard;
    private bool _navPaneAnimating;
    private string _activePrimaryNavKey = MainNavItemKeys.Start;
    private bool _suppressNavSelectionChanged;
    private long _navSelectionRequestId;
    private readonly ShellPanePolicy _panePolicy = new();
#if WINDOWS
    private TrayIconManager? _trayIcon;
    private bool _allowClose;
#endif
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
#if WINDOWS
        _trayIcon?.Dispose();
        _trayIcon = null;
#endif
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

#if WINDOWS
        if (e.PropertyName == nameof(Preferences.MinimizeToTray))
        {
            UpdateTrayState();
        }
#endif
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
        if (UiMotion.Current.IsAnimationEnabled)
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
        var transition = UiMotion.Current.IsAnimationEnabled
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
        var transition = UiMotion.Current.IsAnimationEnabled
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
        if (RightPanelColumnDefinition is not null)
        {
            RightPanelColumnDefinition.Width = GridLength.Auto;
        }
        var baseWidth = double.IsNaN(RightPanelColumn.Width) || RightPanelColumn.Width <= 0
            ? _rightPanelLastWidth
            : RightPanelColumn.Width;
        var targetWidth = Math.Clamp(baseWidth, RightPanelMinWidth, RightPanelMaxWidth);
        _rightPanelLastWidth = targetWidth;

        if (UiMotion.Current.IsAnimationEnabled)
        {
            RightPanelColumn.Visibility = Visibility.Visible;
            RightPanelColumn.Width = 0;
            RightPanelColumn.Opacity = 0;
            if (RightPanelTranslate is not null)
            {
                RightPanelTranslate.X = RightPanelAnimationOffset;
            }
            AnimateRightPanel(open: true, fromWidth: 0, toWidth: targetWidth);
        }
        else
        {
            RightPanelColumn.Visibility = Visibility.Visible;
            RightPanelColumn.Width = targetWidth;
            RightPanelColumn.Opacity = 1;
            if (RightPanelTranslate is not null)
            {
                RightPanelTranslate.X = 0;
            }
        }

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
        if (UiMotion.Current.IsAnimationEnabled)
        {
            var fromWidth = RightPanelColumn.Width;
            if (double.IsNaN(fromWidth) || fromWidth <= 0)
            {
                fromWidth = RightPanelColumn.ActualWidth;
            }
            if (fromWidth <= 0)
            {
                fromWidth = _rightPanelLastWidth;
            }
            _rightPanelLastWidth = Math.Clamp(fromWidth, RightPanelMinWidth, RightPanelMaxWidth);
            AnimateRightPanel(open: false, fromWidth: fromWidth, toWidth: 0);
        }
        else
        {
            RightPanelColumn.Visibility = Visibility.Collapsed;
            if (RightPanelColumnDefinition is not null)
            {
                RightPanelColumnDefinition.Width = new GridLength(0);
            }
            RightPanelColumn.Width = 0;
            RightPanelColumn.Opacity = 1;
            if (RightPanelTranslate is not null)
            {
                RightPanelTranslate.X = 0;
            }
        }

        DiffPanel.Visibility = Visibility.Collapsed;
        TodoPanel.Visibility = Visibility.Collapsed;

        DiffPanelButton.IsChecked = false;
        TodoPanelButton.IsChecked = false;
    }

    private void UpdateRightPanelAvailability(bool isChat)
    {
        var visibility = isChat ? Visibility.Visible : Visibility.Collapsed;
        DiffPanelButton.Visibility = visibility;
        TodoPanelButton.Visibility = visibility;

        if (RightPanelColumnDefinition is not null)
        {
            if (!isChat)
            {
                RightPanelColumnDefinition.Width = new GridLength(0);
                if (RightPanelColumn is not null)
                {
                    RightPanelColumn.Visibility = Visibility.Collapsed;
                    RightPanelColumn.Width = 0;
                    RightPanelColumn.Opacity = 1;
                    if (RightPanelTranslate is not null)
                    {
                        RightPanelTranslate.X = 0;
                    }
                }
            }
        }

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
        _rightPanelLastWidth = newWidth;
        e.Handled = true;
    }

    private void AnimateRightPanel(bool open, double fromWidth, double toWidth)
    {
        if (RightPanelColumn is null)
        {
            return;
        }

        _rightPanelStoryboard?.Stop();

        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var widthAnimation = new DoubleAnimation
        {
            From = fromWidth,
            To = toWidth,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(widthAnimation, RightPanelColumn);
        Storyboard.SetTargetProperty(widthAnimation, "Width");

        var opacityAnimation = new DoubleAnimation
        {
            From = open ? 0 : 1,
            To = open ? 1 : 0,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(opacityAnimation, RightPanelColumn);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

        var translateAnimation = new DoubleAnimation
        {
            From = open ? RightPanelAnimationOffset : 0,
            To = open ? 0 : RightPanelAnimationOffset,
            Duration = duration,
            EasingFunction = easing
        };
        if (RightPanelTranslate is not null)
        {
            Storyboard.SetTarget(translateAnimation, RightPanelTranslate);
            Storyboard.SetTargetProperty(translateAnimation, "X");
        }

        var storyboard = new Storyboard();
        storyboard.Children.Add(widthAnimation);
        storyboard.Children.Add(opacityAnimation);
        if (RightPanelTranslate is not null)
        {
            storyboard.Children.Add(translateAnimation);
        }

        storyboard.Completed += (_, _) =>
        {
            if (!open)
            {
                RightPanelColumn.Visibility = Visibility.Collapsed;
                if (RightPanelColumnDefinition is not null)
                {
                    RightPanelColumnDefinition.Width = new GridLength(0);
                }
                RightPanelColumn.Width = 0;
                RightPanelColumn.Opacity = 1;
                if (RightPanelTranslate is not null)
                {
                    RightPanelTranslate.X = 0;
                }
            }
            _rightPanelStoryboard = null;
        };

        _rightPanelStoryboard = storyboard;
        storyboard.Begin();
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
        SyncSessionsHeaderPaneState(MainNavView?.IsPaneOpen ?? true);
        NavVM.RebuildTree();
#if WINDOWS
        InitializeTray();
#endif
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
        var targetOpen = _panePolicy.Toggle(MainNavView.IsPaneOpen);

        if (!UiMotion.Current.IsAnimationEnabled || MainNavView.DisplayMode == NavigationViewDisplayMode.Minimal)
        {
            MainNavView.IsPaneOpen = targetOpen;
            SyncSessionsHeaderPaneState(targetOpen);
            UpdateNavPaneToggleUi(targetOpen);
            return;
        }

        AnimateNavPane(targetOpen);
        UpdateNavPaneToggleUi(targetOpen);
    }

    private void UpdateNavPaneToggleUi(bool? isOpenOverride = null)
    {
        if (MainNavView == null || TitleBarToggleLeftNavButton == null)
        {
            return;
        }

        var isOpen = isOpenOverride ?? MainNavView.IsPaneOpen;
        ToolTipService.SetToolTip(TitleBarToggleLeftNavButton, isOpen ? "折叠左侧边栏" : "展开左侧边栏");
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
        SyncSessionsHeaderPaneState(sender.IsPaneOpen);
    }

    private void OnMainNavPaneClosed(NavigationView sender, object args)
    {
        UpdateNavPaneToggleUi();
        SyncSessionsHeaderPaneState(sender.IsPaneOpen);
    }

    private void SyncSessionsHeaderPaneState(bool isOpen)
    {
        if (NavVM?.SessionsHeaderItem == null)
        {
            return;
        }

        NavVM.SessionsHeaderItem.IsPaneOpen = isOpen;
    }

    private void AnimateNavPane(bool targetOpen)
    {
        if (MainNavView == null)
        {
            return;
        }

        if (_navPaneAnimating)
        {
            _navPaneStoryboard?.Stop();
            _navPaneAnimating = false;
        }

        var from = targetOpen ? DefaultCompactPaneLength : MainNavView.OpenPaneLength;
        var to = targetOpen ? DefaultOpenPaneLength : DefaultCompactPaneLength;

        if (targetOpen)
        {
            MainNavView.IsPaneOpen = true;
        }

        MainNavView.OpenPaneLength = from;

        var duration = TimeSpan.FromMilliseconds(NavPaneAnimationDurationMs);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var widthAnimation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(widthAnimation, MainNavView);
        Storyboard.SetTargetProperty(widthAnimation, "OpenPaneLength");

        var storyboard = new Storyboard();
        storyboard.Children.Add(widthAnimation);
        storyboard.Completed += (_, _) =>
        {
            _navPaneAnimating = false;
            if (!targetOpen)
            {
                MainNavView.IsPaneOpen = false;
                MainNavView.OpenPaneLength = DefaultOpenPaneLength;
            }
            else
            {
                MainNavView.OpenPaneLength = DefaultOpenPaneLength;
            }
            UpdateNavPaneToggleUi();
            _navPaneStoryboard = null;
        };

        _navPaneAnimating = true;
        _navPaneStoryboard = storyboard;
        storyboard.Begin();
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
    private void InitializeTray()
    {
        UpdateTrayState();

        var window = App.MainWindowInstance;
        if (window?.AppWindow != null)
        {
            window.AppWindow.Closing -= OnAppWindowClosing;
            window.AppWindow.Closing += OnAppWindowClosing;
        }
    }

    private void UpdateTrayState()
    {
        if (!Preferences.IsMinimizeToTraySupported)
        {
            DisposeTray();
            return;
        }

        if (!Preferences.MinimizeToTray)
        {
            DisposeTray();
            ShowMainWindow();
            return;
        }

        EnsureTrayIcon();
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

    private void DisposeTray()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
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
        DisposeTray();
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
#endif
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
