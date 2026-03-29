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
using SalmonEgg.Presentation.Navigation;
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

    private bool _isNavItemsSubscribed;
    private readonly List<ObservableCollection<MainNavItemViewModel>> _projectChildCollections = new();
    private readonly DeferredActionGate<string> _archiveOnFlyoutClosed = new(StringComparer.Ordinal);
    private readonly DeferredActionGate<string> _moveOnFlyoutClosed = new(StringComparer.Ordinal);
    private readonly DeferredActionGate<string> _renameOnFlyoutClosed = new(StringComparer.Ordinal);
    private string? _pendingArchiveSessionId;
    private string? _pendingMoveSessionId;
    private string? _pendingRenameSessionId;
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
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly MainNavigationContentSyncAdapter _mainNavigationContentSyncAdapter;
    private readonly MainNavigationViewAdapter _mainNavigationViewAdapter;
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
        _navigationCoordinator = App.ServiceProvider.GetRequiredService<INavigationCoordinator>();

        this.InitializeComponent();
        _mainNavigationContentSyncAdapter = new MainNavigationContentSyncAdapter(_navigationCoordinator);
        _mainNavigationViewAdapter = new MainNavigationViewAdapter(MainNavView, DispatcherQueue, NavVM, _navigationCoordinator);
        BootLogDebug("MainPage: InitializeComponent done");

        Loaded += OnMainPageLoaded;
        Unloaded += OnMainPageUnloaded;
        ContentFrame.Navigated += OnContentFrameNavigated;

        // 2. Listen for global preference changes (animations, theme, backdrop)
        Preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _chatViewModel.PropertyChanged += OnChatViewModelPropertyChanged;
        NavVM.PropertyChanged += OnNavigationViewModelPropertyChanged;

        // 3. Initialize theme and motion state
        ApplyTheme();
        ApplyBackdrop();
        SubscribeNavItems();
        // NavVM.PropertyChanged registration removed as layout is now driven by LayoutVM SSOT

        // 4. Default to Start view on launch
        EnsureStartContent();
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
        Preferences.PropertyChanged -= OnPreferencesPropertyChanged;
        _chatViewModel.PropertyChanged -= OnChatViewModelPropertyChanged;
        NavVM.PropertyChanged -= OnNavigationViewModelPropertyChanged;
        _metricsProvider.Detach();
#if WINDOWS
        _trayIcon?.Dispose();
        _trayIcon = null;
#endif
    }

    public void NavigateToChat()
    {
        EnsureChatContent();
    }

    public void NavigateToStart()
    {
        EnsureStartContent();
    }

    public void NavigateToDiscoverSessions()
    {
        EnsureDiscoverSessionsContent();
    }

    public void NavigateToSettingsSubPage(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            key = "General";
        }

        EnsureSettingsContent(key);
        _mainNavigationViewAdapter.ApplySelectionDeferred();
    }

    private static Type GetSettingsShellPageType() => typeof(SalmonEgg.Presentation.Views.SettingsShellPage);

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
#else
        // Cross-platform fallback "Mica-like" backdrop.
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AppBackdropBrush", out var brush) && brush is Brush b)
        {
            Background = b;
        }
#endif
    }

    private void SubscribeNavItems()
    {
        if (_isNavItemsSubscribed)
        {
            return;
        }

        NavVM.Items.CollectionChanged += OnNavItemsChanged;
        RefreshProjectChildSubscriptions();
        _isNavItemsSubscribed = true;
    }

    private void UnsubscribeNavItems()
    {
        if (!_isNavItemsSubscribed)
        {
            return;
        }

        NavVM.Items.CollectionChanged -= OnNavItemsChanged;
        ClearProjectChildSubscriptions();
        _isNavItemsSubscribed = false;
    }

    private void OnNavItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshProjectChildSubscriptions();
        _mainNavigationViewAdapter.ApplySelectionDeferred();
    }

    private void OnProjectChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _mainNavigationViewAdapter.ApplySelectionDeferred();
    }

    private void RefreshProjectChildSubscriptions()
    {
        ClearProjectChildSubscriptions();

        foreach (var project in NavVM.Items.OfType<ProjectNavItemViewModel>())
        {
            project.Children.CollectionChanged += OnProjectChildrenChanged;
            _projectChildCollections.Add(project.Children);
        }
    }

    private void ClearProjectChildSubscriptions()
    {
        foreach (var children in _projectChildCollections)
        {
            children.CollectionChanged -= OnProjectChildrenChanged;
        }

        _projectChildCollections.Clear();
    }

    private void NavigateTo(Type pageType, object? parameter = null)
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

        if (await _mainNavigationViewAdapter.HandleItemInvokedAsync(args))
        {
            return;
        }
        
        BootLogDebug("MainNav ItemInvoked: no adapter route matched.");
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
        if (!string.IsNullOrWhiteSpace(_pendingArchiveSessionId))
        {
            var sessionId = _pendingArchiveSessionId;
            _pendingArchiveSessionId = null;
            _archiveOnFlyoutClosed.TryConsume(sessionId);
        }

        if (!string.IsNullOrWhiteSpace(_pendingMoveSessionId))
        {
            var sessionId = _pendingMoveSessionId;
            _pendingMoveSessionId = null;
            _moveOnFlyoutClosed.TryConsume(sessionId);
        }

        if (!string.IsNullOrWhiteSpace(_pendingRenameSessionId))
        {
            var sessionId = _pendingRenameSessionId;
            _pendingRenameSessionId = null;
            _renameOnFlyoutClosed.TryConsume(sessionId);
        }
    }

    private void OnSessionMoveMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.CommandParameter is not SessionNavItemViewModel session)
        {
            return;
        }

        if (session.IsPlaceholder || string.IsNullOrWhiteSpace(session.SessionId))
        {
            return;
        }

        _pendingMoveSessionId = session.SessionId;
        _moveOnFlyoutClosed.Request(session.SessionId, () =>
        {
            _ = DispatcherQueue.TryEnqueue(() => _ = session.MoveCommand.ExecuteAsync(null));
        });
    }

    private void OnSessionRenameMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.CommandParameter is not SessionNavItemViewModel session)
        {
            return;
        }

        if (session.IsPlaceholder || string.IsNullOrWhiteSpace(session.SessionId))
        {
            return;
        }

        _pendingRenameSessionId = session.SessionId;
        _renameOnFlyoutClosed.Request(session.SessionId, () =>
        {
            _ = DispatcherQueue.TryEnqueue(() => _ = session.RenameCommand.ExecuteAsync(null));
        });
    }

    private void EnsureChatContent()
    {
        if (ContentFrame?.CurrentSourcePageType != typeof(ChatView))
        {
            NavigateTo(typeof(ChatView));
        }

        UpdateRightPanelAvailability(true);
        UpdateBackButtonState();
    }

    private void ResetChatAuxiliaryPanelsOnChatExit()
    {
        _metricsSink.ReportClearAuxiliaryPanels();
    }

    private void EnsureDiscoverSessionsContent()
    {
        var pageType = typeof(SalmonEgg.Presentation.Views.Discover.DiscoverSessionsPage);
        if (ContentFrame?.CurrentSourcePageType != pageType)
        {
            NavigateTo(pageType);
        }

        UpdateRightPanelAvailability(false);
        UpdateBackButtonState();
    }

    private void EnsureStartContent()
    {
        if (ContentFrame?.CurrentSourcePageType != typeof(StartView))
        {
            NavigateTo(typeof(StartView));
        }

        UpdateRightPanelAvailability(false);
        UpdateBackButtonState();
    }

    private void EnsureSettingsContent(string key)
    {
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
                _metricsSink.ReportToggleRightPanel(targetMode);
            }
        }
    }

    private void OnBottomPanelButtonClick(object sender, RoutedEventArgs e)
    {
        if (!IsChatPageActive())
        {
            return;
        }

        _metricsSink.ReportToggleBottomPanel();
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

    private DataTemplate GetBottomPanelButtonIconTemplate(BottomPanelMode mode)
        => GetAuxiliaryIconTemplate(mode == BottomPanelMode.Dock
            ? "BottomPanelTitleBarFilledIconTemplate"
            : "BottomPanelTitleBarRegularIconTemplate");

    private DataTemplate GetDiffPanelButtonIconTemplate(RightPanelMode mode)
        => GetAuxiliaryIconTemplate(mode == RightPanelMode.Diff
            ? "DiffPanelTitleBarFilledIconTemplate"
            : "DiffPanelTitleBarRegularIconTemplate");

    private DataTemplate GetTodoPanelButtonIconTemplate(RightPanelMode mode)
        => GetAuxiliaryIconTemplate(mode == RightPanelMode.Todo
            ? "TodoPanelTitleBarFilledIconTemplate"
            : "TodoPanelTitleBarRegularIconTemplate");

    private static DataTemplate GetAuxiliaryIconTemplate(string resourceKey)
    {
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(resourceKey, out var value) && value is DataTemplate template)
        {
            return template;
        }

        throw new InvalidOperationException($"Missing auxiliary icon template resource '{resourceKey}'.");
    }

    // Behavior notes:
    // 1) Auxiliary title bar buttons only appear on chat pages.
    private void UpdateRightPanelAvailability(bool isChat)
    {
        var visibility = isChat ? Visibility.Visible : Visibility.Collapsed;
        BottomPanelButton.Visibility = visibility;
        DiffPanelButton.Visibility = visibility;
        TodoPanelButton.Visibility = visibility;
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

    private async void OnMainPageLoaded(object sender, RoutedEventArgs e)
    {
        ConfigureTitleBar();
#if WINDOWS
        _metricsProvider.Attach(App.MainWindowInstance!, _appWindowTitleBar);
#else
        _metricsProvider.Attach(App.MainWindowInstance!, null);
#endif
        UpdateNavPaneToggleUi();
        NavVM.RebuildTree();
        _mainNavigationViewAdapter.ApplySelection();
        _mainNavigationViewAdapter.ApplySelectionDeferred();
#if WINDOWS
        InitializeTray();
#endif
        await _chatViewModel.RestoreAsync();
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
        BootLogDebug($"ContentFrame Navigated: {e.SourcePageType?.Name ?? "<null>"}");
        if (!IsChatPageType(e.SourcePageType))
        {
            ResetChatAuxiliaryPanelsOnChatExit();
        }

        UpdateBackButtonState();
        _mainNavigationContentSyncAdapter.OnFrameNavigated(e.SourcePageType);
        // Selection sync is handled by OnNavigationViewModelPropertyChanged
        // when the coordinator updates the shell selection state. Calling
        // ApplySelectionDeferred here would interrupt the indicator animation.
        UpdateRightPanelAvailability(IsChatPageType(e.SourcePageType));
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

    private void OnMainNavPaneOpened(NavigationView sender, object args)
    {
        UpdateNavPaneToggleUi();
        _mainNavigationViewAdapter.ApplySelectionDeferred();
    }

    private void OnMainNavPaneClosed(NavigationView sender, object args)
    {
        UpdateNavPaneToggleUi();
        _mainNavigationViewAdapter.ApplySelectionDeferred();
    }

    private void OnMainNavDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        UpdateNavPaneToggleUi();
        _mainNavigationViewAdapter.ApplySelectionDeferred();
    }

    private void OnMainNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        BootLogDebug($"MainNav SelectionChanged: selected={DescribeNavSelection(sender.SelectedItem)} settings={args.IsSettingsSelected}");
        // Do NOT re-drive SelectedItem here. The ViewModel-driven
        // ApplySelection() (from OnNavigationViewModelPropertyChanged) is the
        // authoritative path. Re-applying during NavigationView's own selection
        // processing interrupts the native indicator slide animation.
    }

    private void OnNavigationViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainNavigationViewModel.SelectedItem) ||
            e.PropertyName == nameof(MainNavigationViewModel.ProjectedControlSelectedItem) ||
            e.PropertyName == nameof(MainNavigationViewModel.IsSettingsSelected))
        {
            BootLogDebug($"NavVM ProjectionChanged: current={NavVM.CurrentSelection}; projected={DescribeNavSelection(NavVM.ProjectedControlSelectedItem)}; settings={NavVM.IsSettingsSelected}");
            _mainNavigationViewAdapter.ApplySelection();
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

    // Manual resizer positioning removed as it is now handled by XAML binding to LayoutVM.LeftNavResizerLeft

    private static string DescribeNavSelection(object? selection) => selection switch
    {
        StartNavItemViewModel => "Start",
        SessionNavItemViewModel session => $"Session:{session.SessionId}",
        ProjectNavItemViewModel project => $"Project:{project.ProjectId}",
        MoreSessionsNavItemViewModel more => $"More:{more.ProjectId}",
        null => "<null>",
        _ => selection.GetType().Name
    };

    private bool IsChatPageActive()
        => IsChatPageType(ContentFrame?.CurrentSourcePageType);

    private static bool IsChatPageType(Type? pageType)
        => pageType == typeof(ChatView);

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
    }
}
