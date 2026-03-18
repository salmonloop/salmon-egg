using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#if WINDOWS
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Windows.Foundation;
using Windows.Graphics;
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
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services;
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

    private string _activePrimaryNavKey = MainNavItemKeys.Start;
    private long _navSelectionRequestId;
    private bool _isMotionSubscribed;
    private bool _isNavItemsSubscribed;
    private readonly DeferredActionGate<string> _archiveOnFlyoutClosed = new(StringComparer.Ordinal);
    private string? _pendingArchiveSessionId;
#if WINDOWS
    private TrayIconManager? _trayIcon;
    private bool _allowClose;
#endif
#if WINDOWS
    private AppWindowTitleBar? _appWindowTitleBar;
    private InputNonClientPointerSource? _titleBarPointerSource;
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
        // 1. Get ViewModels before InitializeComponent to ensure x:Bind works correctly
        Preferences = App.ServiceProvider.GetRequiredService<AppPreferencesViewModel>();
        NavVM = App.ServiceProvider.GetRequiredService<MainNavigationViewModel>();
        _chatViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();
        SearchVM = App.ServiceProvider.GetRequiredService<GlobalSearchViewModel>();

        LayoutVM = App.ServiceProvider.GetRequiredService<ShellLayoutViewModel>();
        _metricsProvider = App.ServiceProvider.GetRequiredService<WindowMetricsProvider>();
        _metricsSink = App.ServiceProvider.GetRequiredService<IShellLayoutMetricsSink>();

        this.InitializeComponent();
        BootLogDebug("MainPage: InitializeComponent done");

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

        // 2. Listen for global preference changes (animations, theme, backdrop)
        Preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _chatViewModel.PropertyChanged += OnChatViewModelPropertyChanged;
        LayoutVM.PropertyChanged += OnLayoutViewModelPropertyChanged;
        NavVM.PropertyChanged += OnNavigationViewModelPropertyChanged;

        // 3. Initialize theme and motion state
        ApplyTheme();
        ApplyBackdrop();
        UpdateNavigationTransitions();
        BootLogDebug("MainPage: transitions updated");

        SubscribeMotion();
        SubscribeNavItems();
        // NavVM.PropertyChanged registration removed as layout is now driven by LayoutVM SSOT

        // 4. Default to Start view on launch
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
        LayoutVM.PropertyChanged -= OnLayoutViewModelPropertyChanged;
        NavVM.PropertyChanged -= OnNavigationViewModelPropertyChanged;
        _metricsProvider.Detach();
#if WINDOWS
        _trayIcon?.Dispose();
        _trayIcon = null;
#endif
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

            MainNavView.SelectedItem = MainNavView.SettingsItem;
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
        // Dynamically enable/disable Frame transitions based on global settings
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

    private async void OnMainNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
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
            if (await TryHandleNavItemTagAsync(tag))
            {
                return;
            }
        }

        if (ReferenceEquals(args.InvokedItemContainer, sender.SettingsItem))
        {
            NavVM.SelectSettings();
            NavigateToSettingsSubPage("General");
            return;
        }

        var invoked = ResolveInvokedItem(args);
        BootLogDebug($"MainNav ItemInvoked: resolved={invoked?.GetType().Name ?? "null"}");
        if (invoked is StartNavItemViewModel)
        {
            NavVM.SelectStart();
            EnsureStartContent();
            return;
        }

        if (invoked is SessionNavItemViewModel session && !session.IsPlaceholder)
        {
            await ActivateSessionAsync(session.SessionId, session.ProjectId);
            return;
        }

        if (invoked is ProjectNavItemViewModel project)
        {
            NavVM.ToggleProjectExpanded(project.ProjectId);
            ApplyMainNavSelectionDeferred();
            return;
        }

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

    private async Task<bool> TryHandleNavItemTagAsync(string tag)
    {
        if (string.Equals(tag, NavItemTag.Start, StringComparison.Ordinal))
        {
            NavVM.SelectStart();
            EnsureStartContent();
            return true;
        }

        if (NavItemTag.TryParseSession(tag, out var sessionId))
        {
            var session = NavVM.Items
                .OfType<ProjectNavItemViewModel>()
                .SelectMany(project => project.Children.OfType<SessionNavItemViewModel>())
                .FirstOrDefault(item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal));

            await ActivateSessionAsync(sessionId, session?.ProjectId);
            return true;
        }

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

    private async Task ActivateSessionAsync(string sessionId, string? projectId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        NavVM.SelectSession(sessionId);
        EnsureChatContent();

        Preferences.LastSelectedProjectId = string.Equals(projectId, MainNavigationViewModel.UnclassifiedProjectId, StringComparison.Ordinal)
            ? null
            : projectId;

        await _chatViewModel.TrySwitchToSessionAsync(sessionId);
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




    private string GetRightPanelTitle(RightPanelMode mode, string? planTitle)
    {
        return mode switch
        {
            RightPanelMode.Diff => "Diff",
            RightPanelMode.Todo => string.IsNullOrWhiteSpace(planTitle) ? "Todo" : planTitle,
            _ => "Panel"
        };
    }

    private void UpdateRightPanelAvailability(bool isChat)
    {
        var visibility = isChat ? Visibility.Visible : Visibility.Collapsed;
        DiffPanelButton.Visibility = visibility;
        TodoPanelButton.Visibility = visibility;

        if (!isChat && LayoutVM.RightPanelMode != RightPanelMode.None)
        {
            _metricsSink.ReportRightPanelMode(RightPanelMode.None);
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
        ApplyNavigationViewState();
        UpdateNavPaneToggleUi();
        NavVM.RebuildTree();
        ApplyMainNavSelection();
        ApplyNavItemTransitionsDeferred();
#if WINDOWS
        InitializeTray();
#endif
    }

    private void OnAppTitleBarLoaded(object sender, RoutedEventArgs e)
    {
#if WINDOWS
        UpdateTitleBarInteractiveRegions();
#endif
    }

    private void OnAppTitleBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
#if WINDOWS
        UpdateTitleBarInteractiveRegions();
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

    private async void OnToggleLeftNavClick(object sender, RoutedEventArgs e)
    {
        await _metricsSink.ReportNavToggle("TitleBarButton");
    }

    private void OnSearchPanelPopupOpened(object sender, object e)
    {
        // Flyout positioning is handled automatically by the system
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
            // Move focus to background or Frame to dismiss search keyboard if needed
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
        ToolTipService.SetToolTip(TitleBarToggleLeftNavButton, isOpen ? "Collapse Sidebar" : "Expand Sidebar");
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
            NavVM.SelectSettings();
            return;
        }
    }

    private void OnMainNavPaneOpened(NavigationView sender, object args)
    {
        UpdateNavPaneToggleUi();
        ApplyMainNavSelectionDeferred();
    }

    private void OnMainNavPaneClosed(NavigationView sender, object args)
    {
        UpdateNavPaneToggleUi();
        ApplyMainNavSelectionDeferred();
    }

    private void OnMainNavDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        UpdateNavPaneToggleUi();
        ApplyMainNavSelectionDeferred();
    }

    private void OnLayoutViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellLayoutViewModel.IsNavPaneOpen) ||
            e.PropertyName == nameof(ShellLayoutViewModel.NavPaneDisplayMode))
        {
            ApplyNavigationViewState();
        }
    }

    private void OnNavigationViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainNavigationViewModel.SelectedItem) ||
            e.PropertyName == nameof(MainNavigationViewModel.IsSettingsSelected))
        {
            ApplyMainNavSelection();
        }
    }

    private void OnChatViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (LayoutVM.RightPanelMode != RightPanelMode.Todo)
        {
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.CurrentPlanTitle))
        {
            // The title is now bound in XAML using GetRightPanelTitle.
            // comunitToolkit.Mvvm will notify property changes.
        }
    }

    // Animation logic removed

    private void OnMainNavPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
    {
        // The view must follow the store snapshot. If the shell still wants the pane open,
        // reject spontaneous UI closes in expanded/compact modes so NavigationView cannot drift
        // away from the SSOT store state.
        args.Cancel = ShellPanePolicy.ShouldCancelClosing(
            desiredPaneOpen: LayoutVM?.IsNavPaneOpen == true,
            isMinimalMode: sender.DisplayMode == NavigationViewDisplayMode.Minimal);
    }

    private void ApplyNavigationViewState()
    {
        if (MainNavView is null)
        {
            return;
        }

        MainNavView.PaneDisplayMode = ResolveNavigationViewPaneDisplayMode();
        MainNavView.IsPaneOpen = LayoutVM.IsNavPaneOpen;
    }

    private void ApplyMainNavSelection()
    {
        if (MainNavView is null)
        {
            return;
        }

        var target = ResolveMainNavSelectedItem();
        if (ReferenceEquals(target, MainNavView.SettingsItem))
        {
            SetSelectedSettingsItemDeferred();
            return;
        }

        if (target is null)
        {
            return;
        }

        if (ReferenceEquals(MainNavView.SelectedItem, target))
        {
            return;
        }

        MainNavView.SelectedItem = target;
    }

    private object? ResolveMainNavSelectedItem()
    {
        if (MainNavView is null)
        {
            return null;
        }

        if (NavVM.IsSettingsSelected)
        {
            return MainNavView.SettingsItem;
        }

        var target = NavVM.SelectedItem;
        if (target is not SessionNavItemViewModel sessionItem)
        {
            return target;
        }

        if (LayoutVM.IsNavPaneOpen)
        {
            return sessionItem;
        }

        return (object?)NavVM.Items
            .OfType<ProjectNavItemViewModel>()
            .FirstOrDefault(project => string.Equals(project.ProjectId, sessionItem.ProjectId, StringComparison.Ordinal))
            ?? sessionItem;
    }

    private void ApplyMainNavSelectionDeferred()
    {
        _ = DispatcherQueue.TryEnqueue(ApplyMainNavSelection);
    }

    private NavigationViewPaneDisplayMode ResolveNavigationViewPaneDisplayMode()
        => LayoutVM.NavPaneDisplayMode switch
        {
            SalmonEgg.Presentation.Core.Mvux.ShellLayout.NavigationPaneDisplayMode.Expanded when LayoutVM.IsNavPaneOpen => NavigationViewPaneDisplayMode.Left,
            SalmonEgg.Presentation.Core.Mvux.ShellLayout.NavigationPaneDisplayMode.Expanded => NavigationViewPaneDisplayMode.LeftCompact,
            SalmonEgg.Presentation.Core.Mvux.ShellLayout.NavigationPaneDisplayMode.Compact => NavigationViewPaneDisplayMode.LeftCompact,
            SalmonEgg.Presentation.Core.Mvux.ShellLayout.NavigationPaneDisplayMode.Minimal => NavigationViewPaneDisplayMode.LeftMinimal,
            _ => NavigationViewPaneDisplayMode.Auto
        };

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
            // Use the full custom title bar and mark interactive controls as passthrough regions.
            window.SetTitleBar(AppTitleBar);
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
        _titleBarPointerSource = InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id);
        UpdateTitleBarInteractiveRegions();
#endif
    }

#if WINDOWS
    private void UpdateTitleBarInteractiveRegions()
    {
        if (AppTitleBar is null || _titleBarPointerSource is null || AppTitleBar.XamlRoot is null)
        {
            return;
        }

        var regions = new List<RectInt32>();

        TryAddInteractiveRegion(TitleBarLeftButtons, regions);
        TryAddInteractiveRegion(TopSearchBox, regions);
        TryAddInteractiveRegion(TitleBarRightButtons, regions);

        _titleBarPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, regions.ToArray());
    }

    private void TryAddInteractiveRegion(FrameworkElement? element, List<RectInt32> regions)
    {
        if (element is null || AppTitleBar is null || AppTitleBar.XamlRoot is null)
        {
            return;
        }

        if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        var transform = element.TransformToVisual(AppTitleBar);
        var origin = transform.TransformPoint(new Point(0, 0));
        var scale = AppTitleBar.XamlRoot.RasterizationScale;

        regions.Add(new RectInt32(
            (int)Math.Round(origin.X * scale),
            (int)Math.Round(origin.Y * scale),
            Math.Max(1, (int)Math.Round(element.ActualWidth * scale)),
            Math.Max(1, (int)Math.Round(element.ActualHeight * scale))));
    }
#endif

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
    // TitleBar insets are now handled by WindowMetricsProvider reporting to IShellLayoutMetricsSink,
    // which updates ShellLayoutStore/ShellLayoutViewModel. Visuals are bound using x:Bind in XAML.
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
        LayoutVM.PropertyChanged -= OnLayoutViewModelPropertyChanged;
    }
}
