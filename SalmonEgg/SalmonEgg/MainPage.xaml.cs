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
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.Views;
using SalmonEgg.Presentation.Views.Chat;
using SalmonEgg.Presentation.Views.Start;
#if WINDOWS
using SalmonEgg.Platforms.Windows;
#endif

namespace SalmonEgg;

public sealed partial class MainPage : Page
{
    private const double NavPaneMinWidth = 240;
    private const double NavPaneMaxWidth = 480;
    private const double NavPaneAnimationDurationMs = 180;
    private const double RightPanelMinWidth = 240;
    private const double RightPanelMaxWidth = 520;
    private const double RightPanelAnimationOffset = 16;
    private bool _isResizingRightPanel;
    private double _rightPanelResizeStartX;
    private double _rightPanelResizeStartWidth;
    private bool _isResizingLeftNav;
    private double _leftNavResizeStartX;
    private double _leftNavResizeStartWidth;

    private Storyboard? _rightPanelStoryboard;
    private Storyboard? _navPaneStoryboard;
    private bool _navPaneAnimating;
    private string _activePrimaryNavKey = MainNavItemKeys.Start;
    private bool _suppressNavSelectionChanged;
    private long _navSelectionRequestId;
    private bool _isMotionSubscribed;
    private bool _isNavItemsSubscribed;
    private readonly DeferredActionGate<string> _archiveOnFlyoutClosed = new(StringComparer.Ordinal);
    private string? _pendingArchiveSessionId;
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
    public GlobalSearchViewModel SearchVM { get; }
    private readonly ChatViewModel _chatViewModel;
    public ChatViewModel ChatVM => _chatViewModel;
    public ShellLayoutViewModel LayoutVM { get; }
    private readonly WindowMetricsProvider _metricsProvider;
    private readonly IShellLayoutMetricsSink _metricsSink;
    private readonly SalmonEgg.Presentation.Logic.SearchInteractionLogic _searchLogic = new();

    public MainPage()
    {
        BootLogDebug("MainPage: ctor start");
        // 1. 鍦ㄥ垵濮嬪寲缁勪欢鍓嶈幏鍙?ViewModel锛岀‘淇?x:Bind 缁戝畾姝ｅ父
        Preferences = App.ServiceProvider.GetRequiredService<AppPreferencesViewModel>();
        NavVM = App.ServiceProvider.GetRequiredService<MainNavigationViewModel>();
        _chatViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();
        SearchVM = App.ServiceProvider.GetRequiredService<GlobalSearchViewModel>();

        LayoutVM = App.ServiceProvider.GetRequiredService<ShellLayoutViewModel>();
        _metricsProvider = App.ServiceProvider.GetRequiredService<WindowMetricsProvider>();
        _metricsSink = App.ServiceProvider.GetRequiredService<IShellLayoutMetricsSink>();

        this.InitializeComponent();
        BootLogDebug("MainPage: InitializeComponent done");

        _searchLogic.Attach(this);

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

        // 2. 鐩戝惉鍏ㄥ眬璁剧疆鍙樺寲锛堝鍔ㄧ敾寮€鍏炽€佷富棰樸€佽儗鏅潗璐級
        Preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _chatViewModel.PropertyChanged += OnChatViewModelPropertyChanged;

        // 3. 鍒濆鍖栦富棰樹笌鍔ㄧ敾鐘舵€?
        ApplyTheme();
        ApplyBackdrop();
        UpdateNavigationTransitions();
        BootLogDebug("MainPage: transitions updated");

        ConfigureNavigationView();
        SubscribeMotion();
        SubscribeNavItems();
        // NavVM.PropertyChanged registration removed as layout is now driven by LayoutVM SSOT

        // 4. 鍚姩鍚庨粯璁よ繘鍏ュ紑濮嬬晫闈?
        NavVM.SelectStart();
        NavigateToStart();
        BootLogDebug("MainPage: navigated to StartView");
    }

    private static void BootLogDebug(string message)
    {
#if DEBUG
        App.BootLog(message);
#endif
    }

    private void OnMainPageUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeNavItems();
        UnsubscribeMotion();
        Preferences.PropertyChanged -= OnPreferencesPropertyChanged;
        _chatViewModel.PropertyChanged -= OnChatViewModelPropertyChanged;
        // NavVM.PropertyChanged unregistration removed
        _metricsProvider.Detach();
        _searchLogic.Detach();
        App.CleanupWebResources();
#if WINDOWS
        _trayIcon?.Dispose();
        _trayIcon = null;
#endif
    }

    private void ConfigureNavigationView()
    {
        // SSOT: These are now driven by LayoutVM bindings.
        // We report initial sizes only if they differ from Layout defaults, 
        // but typically the defaults in LayoutState (300/72) match the app.
        UpdateLeftNavMargin();
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
            ApplyNavItemTransitionsDeferred();
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

    private void OnNavVMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Layout sync is now handled by ShellLayoutStore and LayoutVM bindings
    }

    private void SyncRightPanelWidthFromViewModel()
    {
        if (_isResizingRightPanel || RightPanelColumn is null)
        {
            return;
        }

        if (RightPanelColumn.Visibility != Visibility.Visible)
        {
            return;
        }

        var target = Math.Clamp(NavVM.RightPanelWidth, RightPanelMinWidth, RightPanelMaxWidth);
        if (!double.Equals(RightPanelColumn.Width, target))
        {
            RightPanelColumn.Width = target;
        }
    }

    private void UpdateRightPanelState()
    {
        var mode = NavVM.RightPanelMode;
        if (mode == RightPanelMode.None)
        {
            CloseRightPanel();
        }
        else
        {
            OpenRightPanel(mode);
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
        // 鏍规嵁鍏ㄥ眬璁剧疆鍔ㄦ€佸紑鍚垨鍏抽棴 Frame 鐨勮繃娓″姩鐢?
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

    private void SubscribeMotion()
    {
        if (_isMotionSubscribed)
        {
            return;
        }

        UiMotion.Current.PropertyChanged += OnUiMotionPropertyChanged;
        _isMotionSubscribed = true;
    }

    private void UnsubscribeMotion()
    {
        if (!_isMotionSubscribed)
        {
            return;
        }

        UiMotion.Current.PropertyChanged -= OnUiMotionPropertyChanged;
        _isMotionSubscribed = false;
    }

    private void OnUiMotionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UiMotion.NavItemTransitions))
        {
            ApplyNavItemTransitionsDeferred();
        }
    }

    private void SubscribeNavItems()
    {
        if (_isNavItemsSubscribed)
        {
            return;
        }

        NavVM.Items.CollectionChanged += OnNavItemsChanged;
        _isNavItemsSubscribed = true;
    }

    private void UnsubscribeNavItems()
    {
        if (!_isNavItemsSubscribed)
        {
            return;
        }

        NavVM.Items.CollectionChanged -= OnNavItemsChanged;
        _isNavItemsSubscribed = false;
    }

    private void OnNavItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ApplyNavItemTransitionsDeferred();
    }

    private void ApplyNavItemTransitionsDeferred()
    {
        _ = DispatcherQueue.TryEnqueue(ApplyNavItemTransitions);
    }

    private void ApplyNavItemTransitions()
    {
        if (MainNavView == null)
        {
            return;
        }

        var transitions = UiMotion.Current.NavItemTransitions;
        foreach (var item in FindVisualChildren<NavigationViewItem>(MainNavView))
        {
            item.ContentTransitions = transitions;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void NavigateTo(Type pageType)
    {
        var transition = UiMotion.Current.IsAnimationEnabled
            ? (NavigationTransitionInfo)new EntranceNavigationTransitionInfo()
            : new SuppressNavigationTransitionInfo();
        ContentFrame.Navigate(pageType, null, transition);
    }

    private void NavigateTo(Type pageType, object? parameter)
    {
        var transition = UiMotion.Current.IsAnimationEnabled
            ? (NavigationTransitionInfo)new EntranceNavigationTransitionInfo()
            : new SuppressNavigationTransitionInfo();
        ContentFrame.Navigate(pageType, parameter, transition);
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
        var rawInvokedType = args.InvokedItem?.GetType().Name ?? "null";
        var containerType = args.InvokedItemContainer?.GetType().Name ?? "null";
        var containerDcType = (args.InvokedItemContainer as FrameworkElement)?.DataContext?.GetType().Name ?? "null";
        var invokedDcType = (args.InvokedItem as FrameworkElement)?.DataContext?.GetType().Name ?? "null";
        var tagText = (args.InvokedItemContainer as NavigationViewItem)?.Tag?.ToString() ?? "<null>";
        BootLogDebug($"MainNav ItemInvoked: display={sender.DisplayMode} paneOpen={sender.IsPaneOpen} raw={rawInvokedType} container={containerType} containerDC={containerDcType} invokedDC={invokedDcType} tag={tagText}");

        if (args.InvokedItemContainer is NavigationViewItem navItem
            && navItem.Tag is string tag)
        {
            if (TryHandleNavItemTag(tag))
            {
                return;
            }
        }

        var invoked = ResolveInvokedItem(args);
        BootLogDebug($"MainNav ItemInvoked: resolved={invoked?.GetType().Name ?? "null"}");
        if (invoked is SessionsHeaderNavItemViewModel header)
        {
            BootLogDebug($"MainNav SessionsHeader invoked (paneOpen={header.IsPaneOpen})");
            if (!header.IsPaneOpen)
            {
                BootLogDebug("MainNav SessionsHeader: executing AddProjectCommand");
                _ = header.AddProjectCommand.ExecuteAsync(null);
            }
            else
            {
                BootLogDebug("MainNav SessionsHeader: skipped AddProjectCommand because pane is open");
            }

            return;
        }

        if (invoked is MoreSessionsNavItemViewModel more)
        {
            _ = more.ShowMoreCommand.ExecuteAsync(null);
            return;
        }
    }

    private void OnSessionArchiveMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.CommandParameter is not SessionNavItemViewModel session)
        {
            return;
        }

        if (session.IsPlaceholder || string.IsNullOrWhiteSpace(session.SessionId))
        {
            return;
        }

        _pendingArchiveSessionId = session.SessionId;
        _archiveOnFlyoutClosed.Request(session.SessionId, () =>
        {
            _ = DispatcherQueue.TryEnqueue(() => _ = session.ArchiveCommand.ExecuteAsync(null));
        });
    }

    private void OnSessionNavFlyoutClosed(object sender, object e)
    {
        if (string.IsNullOrWhiteSpace(_pendingArchiveSessionId))
        {
            return;
        }

        var sessionId = _pendingArchiveSessionId;
        _pendingArchiveSessionId = null;
        _archiveOnFlyoutClosed.TryConsume(sessionId);
    }

    private bool TryHandleNavItemTag(string tag)
    {
        if (string.Equals(tag, NavItemTag.SessionsHeader, StringComparison.Ordinal))
        {
            if (!NavVM.SessionsHeaderItem.IsPaneOpen)
            {
                _ = NavVM.SessionsHeaderItem.AddProjectCommand.ExecuteAsync(null);
            }
            else
            {
            }

            return true;
        }

        if (NavItemTag.TryParseMore(tag, out var moreProjectId))
        {
            _ = NavVM.ShowAllSessionsForProjectAsync(moreProjectId);
            return true;
        }

        return false;
    }

    private static object? ResolveInvokedItem(NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is FrameworkElement container && container.DataContext != null)
        {
            return container.DataContext;
        }

        if (args.InvokedItem is FrameworkElement element && element.DataContext != null)
        {
            return element.DataContext;
        }

        return args.InvokedItem;
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
        if (sender is FrameworkElement element && element.Tag is string tag)
        {
            var targetMode = tag switch
            {
                "Diff" => RightPanelMode.Diff,
                "Todo" => RightPanelMode.Todo,
                _ => RightPanelMode.None
            };

            if (targetMode != RightPanelMode.None)
            {
                var newMode = LayoutVM.RightPanelMode == targetMode 
                    ? RightPanelMode.None 
                    : targetMode;
                _metricsSink.ReportRightPanelMode(newMode);
            }
        }
    }



    private void OpenRightPanel(RightPanelMode mode)
    {
        if (RightPanelColumn is null) return;

        UpdateRightPanelTitle();

        if (RightPanelColumnDefinition is not null)
        {
            RightPanelColumnDefinition.Width = GridLength.Auto;
        }

        var targetWidth = Math.Clamp(NavVM.RightPanelWidth, RightPanelMinWidth, RightPanelMaxWidth);
        NavVM.RightPanelWidth = targetWidth;
        
        if (UiMotion.Current.IsAnimationEnabled)
        {
            if (RightPanelColumn.Visibility != Visibility.Visible || RightPanelColumn.ActualWidth == 0)
            {
                RightPanelColumn.Visibility = Visibility.Visible;
                RightPanelColumn.Width = 0;
                RightPanelColumn.Opacity = 0;
                if (RightPanelTranslate is not null) RightPanelTranslate.X = RightPanelAnimationOffset;
                AnimateRightPanel(open: true, fromWidth: 0, toWidth: targetWidth);
            }
            else
            {
                RightPanelColumn.Width = targetWidth;
                RightPanelColumn.Opacity = 1;
                if (RightPanelTranslate is not null) RightPanelTranslate.X = 0;
            }
        }
        else
        {
            RightPanelColumn.Visibility = Visibility.Visible;
            RightPanelColumn.Width = targetWidth;
            RightPanelColumn.Opacity = 1;
            if (RightPanelTranslate is not null) RightPanelTranslate.X = 0;
        }
    }

    private void CloseRightPanel()
    {
        if (RightPanelColumn is null) return;

        if (UiMotion.Current.IsAnimationEnabled)
        {
            var fromWidth = RightPanelColumn.Width;
            if (double.IsNaN(fromWidth) || fromWidth <= 0) fromWidth = RightPanelColumn.ActualWidth;
            if (fromWidth <= 0) fromWidth = NavVM.RightPanelWidth;

            NavVM.RightPanelWidth = Math.Clamp(fromWidth, RightPanelMinWidth, RightPanelMaxWidth);
            AnimateRightPanel(open: false, fromWidth: fromWidth, toWidth: 0);
        }
        else
        {
            RightPanelColumn.Visibility = Visibility.Collapsed;
            RightPanelColumn.Width = 0;
        }
    }

    private void UpdateRightPanelTitle()
    {
        if (RightPanelTitle is null)
        {
            return;
        }

        RightPanelTitle.Text = NavVM.RightPanelMode switch
        {
            RightPanelMode.Diff => "Diff",
            RightPanelMode.Todo => ResolveTodoPanelTitle(),
            _ => "Panel"
        };
    }

    private string ResolveTodoPanelTitle()
    {
        var title = _chatViewModel.CurrentPlanTitle?.Trim();
        return string.IsNullOrWhiteSpace(title) ? "Todo" : title;
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
        if (RightPanelColumn is null || RightPanelResizer is null || LayoutVM.RightPanelVisible == false)
        {
            return;
        }

        _isResizingRightPanel = true;
        _rightPanelResizeStartX = e.GetCurrentPoint(this).Position.X;
        _rightPanelResizeStartWidth = LayoutVM.RightPanelWidth;

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

        _metricsSink.ReportRightPanelWidth(newWidth);
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
#if WINDOWS
        _metricsProvider.Attach(App.MainWindowInstance!, _appWindowTitleBar);
#else
        _metricsProvider.Attach(App.MainWindowInstance!, null);
#endif
        UpdateNavPaneToggleUi();
        NavVM.RebuildTree();
        ApplyNavItemTransitionsDeferred();
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
        _metricsSink.ReportNavToggle("TitleBarButton");
    }

    private void OnSearchPanelPopupOpened(object sender, object e)
    {
        // Flyout 涓嶉渶瑕佹墜鍔ㄨ绠椾綅缃?
    }

    private void OnSearchPanelPopupClosed(object sender, object e)
    {
        if (SearchVM != null)
        {
            SearchVM.IsSearchPanelOpen = false;
        }
    }

    private void ClearSearchFocus()
    {
        if (TopSearchBox != null)
        {
            // 绉婚櫎鐒︾偣鍒拌儗鏅垨 Frame
            ContentFrame?.Focus(FocusState.Programmatic);
        }
    }

    // ToggleNavPane removed as it now simply dispatches via the Click handler.

    private void UpdateNavPaneToggleUi(bool? isOpenOverride = null)
    {
        if (MainNavView == null || TitleBarToggleLeftNavButton == null)
        {
            return;
        }

        var isOpen = isOpenOverride ?? LayoutVM.IsNavPaneOpen;
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
        _metricsSink.ReportNavToggle("PaneOpened");
        UpdateNavPaneToggleUi();
    }

    private void OnMainNavPaneClosed(NavigationView sender, object args)
    {
        _metricsSink.ReportNavToggle("PaneClosed");
        UpdateNavPaneToggleUi();
    }

    private void OnMainNavDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        UpdateNavPaneToggleUi();
    }

    private void OnChatViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (NavVM.RightPanelMode != RightPanelMode.Todo)
        {
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.CurrentPlanTitle))
        {
            UpdateRightPanelTitle();
        }
    }

    // Animation logic removed

    private void OnMainNavPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
    {
        // If the state change was already confirmed in the ViewModel (Single Source of Truth), 
        // we should not interfere with the closing process. This ensures that programmatic 
        // or policy-driven collapses (like at startup) sync correctly with the UI.
        if (LayoutVM != null && !LayoutVM.IsNavPaneOpen)
        {
            return;
        }

        var isMinimal = sender.DisplayMode == NavigationViewDisplayMode.Minimal;
        if (_panePolicy.ShouldCancelClosing(isMinimalMode: isMinimal))
        {
            args.Cancel = true;
        }
    }

    // Manual resizer positioning removed as it is now handled by XAML binding to LayoutVM.LeftNavResizerLeft

    private bool CanResizeLeftNav()
    {
        return LayoutVM != null && LayoutVM.IsNavResizerVisible;
    }

    private void OnLeftNavResizerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (LeftNavResizer == null || MainNavView == null || !CanResizeLeftNav())
        {
            return;
        }

        _isResizingLeftNav = true;
        _leftNavResizeStartX = e.GetCurrentPoint(this).Position.X;
        _leftNavResizeStartWidth = LayoutVM.NavOpenPaneLength;

        LeftNavResizer.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnLeftNavResizerPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizingLeftNav || MainNavView == null)
        {
            return;
        }

        var currentX = e.GetCurrentPoint(this).Position.X;
        var delta = currentX - _leftNavResizeStartX;
        var newWidth = _leftNavResizeStartWidth + delta;

        if (newWidth < NavPaneMinWidth)
        {
            newWidth = NavPaneMinWidth;
        }
        else if (newWidth > NavPaneMaxWidth)
        {
            newWidth = NavPaneMaxWidth;
        }

        _metricsSink.ReportLeftNavWidth(newWidth);
        e.Handled = true;
    }
    
    // Debug line removed

    private void OnLeftNavResizerPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndLeftNavResize(e.Pointer);
        e.Handled = true;
    }

    private void OnLeftNavResizerPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndLeftNavResize(e.Pointer);
    }

    private void EndLeftNavResize(Pointer pointer)
    {
        if (!_isResizingLeftNav || LeftNavResizer == null)
        {
            return;
        }

        _isResizingLeftNav = false;
        LeftNavResizer.ReleasePointerCapture(pointer);
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
        _chatViewModel.PropertyChanged -= OnChatViewModelPropertyChanged;

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
