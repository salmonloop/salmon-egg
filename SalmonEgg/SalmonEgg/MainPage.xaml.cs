using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
#if WINDOWS
using Microsoft.UI.Windowing;
#endif
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
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
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.Views;
using SalmonEgg.Presentation.Views.Chat;
using SalmonEgg.Presentation.Views.Start;
using Windows.ApplicationModel.Resources;
#if WINDOWS
using SalmonEgg.Platforms.Windows;
#endif

namespace SalmonEgg;

public sealed partial class MainPage : Page
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();
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
    // Title bar hosting/interactive-region state is encapsulated by MainWindowTitleBarAdapter.
#endif

    public AppPreferencesViewModel Preferences { get; }
    public MainNavigationViewModel NavVM { get; }
    public GlobalSearchViewModel SearchVM { get; }
    public bool IsGuiAutomationMode { get; }
    private readonly ChatViewModel _chatViewModel;
    public ChatViewModel ChatVM => _chatViewModel;
    public ShellLayoutViewModel LayoutVM { get; }
    private readonly WindowMetricsProvider _metricsProvider;
    private readonly IShellLayoutMetricsSink _metricsSink;
    private readonly ILogger<MainPage> _logger;
    private readonly MainNavigationViewAdapter _mainNavigationViewAdapter;
    private readonly MainWindowTitleBarAdapter _titleBarAdapter;
    private readonly IGamepadInputService _gamepadInputService;
    private readonly IGamepadNavigationDispatcher _gamepadNavigationDispatcher;
    private readonly SalmonEgg.Presentation.Logic.SearchInteractionLogic _searchLogic = new();
    private bool _isGamepadInputAttached;

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
        var navigationCoordinator = App.ServiceProvider.GetRequiredService<INavigationCoordinator>();
        _logger = App.ServiceProvider.GetRequiredService<ILogger<MainPage>>();
        _gamepadInputService = App.ServiceProvider.GetRequiredService<IGamepadInputService>();
        _gamepadNavigationDispatcher = App.ServiceProvider.GetRequiredService<IGamepadNavigationDispatcher>();
        IsGuiAutomationMode = string.Equals(
            Environment.GetEnvironmentVariable("SALMONEGG_GUI"),
            "1",
            StringComparison.Ordinal);

        this.InitializeComponent();
        _mainNavigationViewAdapter = new MainNavigationViewAdapter(NavVM, navigationCoordinator);
        _titleBarAdapter = new MainWindowTitleBarAdapter(
            AppTitleBar,
            AppTitleBarLayoutRoot,
            AppTitleBarContent,
            TitleBarDragRegion,
            TitleBarLeftButtons,
            TopSearchBox,
            TitleBarRightButtons,
            TitleBarToggleLeftNavButton,
            TitleBarBackButton,
            ContentFrame,
            DispatcherQueue,
            _logger);
        BootLogDebug("MainPage: InitializeComponent done");

        Loaded += OnMainPageLoaded;
        Unloaded += OnMainPageUnloaded;
        ContentFrame.Navigated += OnContentFrameNavigated;
        ContentFrame.NavigationFailed += OnContentFrameNavigationFailed;

        // 2. Listen for global preference changes (animations, theme, backdrop)
        Preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _chatViewModel.PropertyChanged += OnChatViewModelPropertyChanged;
        NavVM.PropertyChanged += OnNavigationViewModelPropertyChanged;
        NavVM.TreeRebuilt += OnNavigationTreeRebuilt;
        LayoutVM.PropertyChanged += OnLayoutViewModelPropertyChanged;

        // 3. Initialize theme and motion state
        ApplyTheme();
        ApplyBackdrop();
        // NavVM.PropertyChanged registration removed as layout is now driven by LayoutVM SSOT

        // 4. Default to Start view on launch
        EnsureStartContent();
        BootLogDebug("MainPage: navigated to StartView");
    }

    private async void OnAutomationArchiveSelectedClick(object sender, RoutedEventArgs e)
    {
        if (!IsGuiAutomationMode)
        {
            return;
        }

        var selectedSessionId = (MainNavView.SelectedItem as SessionNavItemViewModel)?.SessionId
            ?? ((MainNavView.SelectedItem as NavigationViewItem)?.DataContext is SessionNavItemViewModel navSession
                ? navSession.SessionId
                : null)
            ?? _chatViewModel.CurrentSessionId;

        if (string.IsNullOrWhiteSpace(selectedSessionId))
        {
            return;
        }

        _ = await _chatViewModel.ArchiveConversationAsync(selectedSessionId).ConfigureAwait(true);
    }

    private static void BootLogDebug(string message)
    {
#if DEBUG
        App.BootLog(message);
#endif
    }

    private void OnMainPageUnloaded(object sender, RoutedEventArgs e)
    {
        DetachGamepadInput();
        Preferences.PropertyChanged -= OnPreferencesPropertyChanged;
        _chatViewModel.PropertyChanged -= OnChatViewModelPropertyChanged;
        NavVM.PropertyChanged -= OnNavigationViewModelPropertyChanged;
        NavVM.TreeRebuilt -= OnNavigationTreeRebuilt;
        LayoutVM.PropertyChanged -= OnLayoutViewModelPropertyChanged;
        _metricsProvider.Detach();
        ContentFrame.NavigationFailed -= OnContentFrameNavigationFailed;
#if WINDOWS
        _titleBarAdapter.Detach();
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

    private void NavigateTo(Type pageType, object? parameter = null)
    {
        var transition = UiMotion.Current.IsAnimationEnabled
            ? (NavigationTransitionInfo)new EntranceNavigationTransitionInfo()
            : new SuppressNavigationTransitionInfo();

        try
        {
            var navigated = ContentFrame.Navigate(pageType, parameter, transition);
            if (!navigated)
            {
                BootLogDebug($"ContentFrame Navigate returned false: target={pageType?.Name ?? "<null>"}");
            }
        }
        catch (Exception ex)
        {
            BootLogDebug($"ContentFrame Navigate exception: target={pageType?.Name ?? "<null>"} exception={ex}");
            throw;
        }
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
        BootLogDebug("Session archive menu clicked.");
        if (sender is not MenuFlyoutItem item || item.CommandParameter is not SessionNavItemViewModel session)
        {
            BootLogDebug("Session archive menu ignored: invalid sender/parameter.");
            return;
        }

        if (session.IsPlaceholder || string.IsNullOrWhiteSpace(session.SessionId))
        {
            BootLogDebug($"Session archive menu ignored: placeholder={session.IsPlaceholder} sessionId={session.SessionId ?? "<null>"}.");
            return;
        }

        BootLogDebug($"Session archive command scheduled: sessionId={session.SessionId}.");
        _pendingArchiveSessionId = null;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            _ = session.ArchiveCommand.ExecuteAsync(null);
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
        _titleBarAdapter.UpdateBackButtonState();
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
        _titleBarAdapter.UpdateBackButtonState();
    }

    private void EnsureStartContent()
    {
        if (ContentFrame?.CurrentSourcePageType != typeof(StartView))
        {
            NavigateTo(typeof(StartView));
        }
        _titleBarAdapter.UpdateBackButtonState();
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
        _titleBarAdapter.UpdateBackButtonState();
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

    private string GetBottomPanelButtonToolTip(bool canToggle)
        => GetAuxiliaryPanelButtonToolTip(
            canToggle,
            "BottomPanelButton.ToolTipService.ToolTip",
            "Bottom Panel");

    private string GetDiffPanelButtonToolTip(bool canToggle)
        => GetAuxiliaryPanelButtonToolTip(
            canToggle,
            "DiffPanelButton.ToolTipService.ToolTip",
            "Diff");

    private string GetTodoPanelButtonToolTip(bool canToggle)
        => GetAuxiliaryPanelButtonToolTip(
            canToggle,
            "TodoPanelButton.ToolTipService.ToolTip",
            "Todo");

    private static string GetAuxiliaryPanelButtonToolTip(
        bool canToggle,
        string enabledResourceKey,
        string enabledFallback)
    {
        return canToggle
            ? ResolveResourceString(enabledResourceKey, enabledFallback)
            : ResolveResourceString(
                "TitleBarAuxiliaryPanelUnavailable.ToolTip",
                "Not available at the current window size");
    }

    private DataTemplate GetAuxiliaryIconTemplate(string resourceKey)
    {
        if (Resources.TryGetValue(resourceKey, out var localValue) && localValue is DataTemplate localTemplate)
        {
            return localTemplate;
        }

        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(resourceKey, out var value) && value is DataTemplate template)
        {
            return template;
        }

        throw new InvalidOperationException($"Missing auxiliary icon template resource '{resourceKey}'.");
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
        AttachGamepadInput();
        _titleBarAdapter.Configure(App.MainWindowInstance);
#if WINDOWS
        _metricsProvider.Attach(App.MainWindowInstance!, _titleBarAdapter.AppWindowTitleBar);
#else
        _metricsProvider.Attach(App.MainWindowInstance!, null);
#endif
        UpdateNavPaneToggleUi();
        NavVM.RebuildTree();
        UpdateMainNavAutomationSelectionState();
        _ = _metricsSink.ReportContentContext(IsChatPageType(ContentFrame?.CurrentSourcePageType));
#if WINDOWS
        InitializeTray();
#endif
        await _chatViewModel.RestoreAsync();
    }

    private void AttachGamepadInput()
    {
        if (_isGamepadInputAttached)
        {
            return;
        }

        _gamepadInputService.IntentRaised += OnGamepadIntentRaised;
        _gamepadInputService.Start();
        _isGamepadInputAttached = true;
    }

    private void DetachGamepadInput()
    {
        if (!_isGamepadInputAttached)
        {
            return;
        }

        _gamepadInputService.IntentRaised -= OnGamepadIntentRaised;
        _gamepadInputService.Stop();
        _isGamepadInputAttached = false;
    }

    private void OnGamepadIntentRaised(object? sender, GamepadNavigationIntent intent)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => OnGamepadIntentRaised(sender, intent));
            return;
        }

        _ = _gamepadNavigationDispatcher.TryDispatch(intent);
    }

    private void OnAppTitleBarLoaded(object sender, RoutedEventArgs e)
    {
#if WINDOWS
        _titleBarAdapter.OnHostLoaded(App.MainWindowInstance);
#endif
    }

    private void OnAppTitleBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
#if WINDOWS
        _titleBarAdapter.OnHostSizeChanged();
#endif
    }

    private void OnContentFrameNavigated(object sender, NavigationEventArgs e)
    {
        BootLogDebug($"ContentFrame Navigated: {e.SourcePageType?.Name ?? "<null>"}");
        if (!IsChatPageType(e.SourcePageType))
        {
            ResetChatAuxiliaryPanelsOnChatExit();
        }

        _titleBarAdapter.UpdateBackButtonState();
        _ = _metricsSink.ReportContentContext(IsChatPageType(e.SourcePageType));
    }

    private void OnContentFrameNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        BootLogDebug($"ContentFrame NavigationFailed: target={e.SourcePageType?.Name ?? "<null>"} exception={e.Exception}");
    }

    private void OnTitleBarBackClick(object sender, RoutedEventArgs e)
    {
        _titleBarAdapter.TryGoBack();
    }

    private async void OnToggleLeftNavClick(object sender, RoutedEventArgs e)
    {
        BootLogDebug($"TitleBar ToggleSidebar Clicked: mode={LayoutVM.NavPaneDisplayMode} open={LayoutVM.IsNavPaneOpen}");
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
        var isOpen = isOpenOverride ?? LayoutVM.IsNavPaneOpen;
        _titleBarAdapter.UpdateNavToggleToolTip(
            ResolveResourceString(
                isOpen ? "TitleBarToggleLeftNavButtonCollapse.ToolTip" : "TitleBarToggleLeftNavButtonExpand.ToolTip",
                isOpen ? "Collapse Sidebar" : "Expand Sidebar"));
    }

    private void OnMainNavPanePresentationChanged(NavigationView sender, object args)
    {
        var controlDisplayMode = args is NavigationViewDisplayModeChangedEventArgs modeChangedArgs
            ? modeChangedArgs.DisplayMode
            : sender.DisplayMode;

        UpdateNavPaneToggleUi(sender.IsPaneOpen);
        BootLogDebug($"MainNav PanePresentationChanged: args={args?.GetType().Name ?? "<null>"} senderPaneOpen={sender.IsPaneOpen} controlMode={controlDisplayMode} layoutMode={LayoutVM.NavPaneDisplayMode} storeOpen={LayoutVM.IsNavPaneOpen}");
        UpdateMainNavAutomationSelectionState();
        _logger.LogDebug(
            "NavView pane event {EventType} DisplayMode={DisplayMode} LayoutMode={LayoutMode} IsPaneOpen={IsPaneOpen} SelectedItem={SelectedItem} SemanticSelection={SemanticSelection} SettingsSelected={IsSettingsSelected}",
            args?.GetType().Name ?? "<null>",
            controlDisplayMode,
            LayoutVM.NavPaneDisplayMode,
            sender.IsPaneOpen,
            DescribeNavSelection(sender.SelectedItem),
            NavVM.CurrentSelection,
            NavVM.IsSettingsSelected);

        if (args is not NavigationViewDisplayModeChangedEventArgs)
        {
            BootLogDebug($"MainNav PanePresentationChanged: reporting pane intent senderPaneOpen={sender.IsPaneOpen} mode={LayoutVM.NavPaneDisplayMode}.");
            _ = _metricsSink.ReportNavPaneOpenIntent(sender.IsPaneOpen, source: "PanePresentationChanged");
        }

    }

    private void OnMainNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        BootLogDebug($"MainNav SelectionChanged: selected={DescribeNavSelection(sender.SelectedItem)} settings={args.IsSettingsSelected}");
        UpdateMainNavAutomationSelectionState();
        _logger.LogDebug(
            "NavView selection changed SelectedItem={SelectedItem} SettingsSelected={IsSettingsSelected} SemanticSelection={SemanticSelection}",
            DescribeNavSelection(sender.SelectedItem),
            args.IsSettingsSelected,
            NavVM.CurrentSelection);

        _ = _mainNavigationViewAdapter.HandleSelectionChangedAsync(sender, args);
    }

    private void OnNavigationViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainNavigationViewModel.IsSettingsSelected))
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                _ = DispatcherQueue.TryEnqueue(() => OnNavigationViewModelPropertyChanged(sender, e));
                return;
            }

            BootLogDebug($"NavVM SettingsChanged: settings={NavVM.IsSettingsSelected}");
            _logger.LogDebug(
                "NavVM settings changed SettingsSelected={IsSettingsSelected}",
                NavVM.IsSettingsSelected);
            UpdateMainNavAutomationSelectionState();
        }
    }

    private void OnNavigationTreeRebuilt(object? sender, EventArgs e)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => OnNavigationTreeRebuilt(sender, e));
            return;
        }

        if (NavVM.IsSettingsSelected)
        {
            return;
        }

        UpdateMainNavAutomationSelectionState();
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

    private static string DescribeSemanticSelection(NavigationSelectionState selection) => selection switch
    {
        NavigationSelectionState.Start => "Start",
        NavigationSelectionState.DiscoverSessions => "DiscoverSessions",
        NavigationSelectionState.Settings => "Settings",
        NavigationSelectionState.Session session => $"Session:{session.SessionId}",
        _ => selection.GetType().Name
    };

    private void UpdateMainNavAutomationSelectionState()
    {
        if (!IsGuiAutomationMode || MainNavAutomationSelectionStateText is null)
        {
            return;
        }

        var state = BuildMainNavAutomationSelectionState();
        MainNavAutomationSelectionStateText.Text = state;
        AutomationProperties.SetName(MainNavAutomationSelectionStateText, state);
    }

    private string BuildMainNavAutomationSelectionState()
    {
        var sessionItem = TryResolveCurrentSessionItem();
        var projectItem = sessionItem is null ? null : TryResolveProjectItem(sessionItem.ProjectId);
        var startItem = NavVM.StartItem;

        var projectContainer = projectItem is null ? null : MainNavView.ContainerFromMenuItem(projectItem) as NavigationViewItem;
        var sessionContainer = sessionItem is null ? null : MainNavView.ContainerFromMenuItem(sessionItem) as NavigationViewItem;
        var startContainer = MainNavView.ContainerFromMenuItem(startItem) as NavigationViewItem;

        var projectVisible = IsContainerVisible(projectContainer);
        var sessionVisible = IsContainerVisible(sessionContainer) && (projectContainer?.IsExpanded ?? true);
        var startVisible = IsContainerVisible(startContainer);
        var projectChildSelected = projectContainer?.IsChildSelected == true;
        var projectSelected = projectContainer?.IsSelected == true;
        var sessionSelected = sessionContainer?.IsSelected == true;
        var startSelected = startContainer?.IsSelected == true;
        var projectExpanded = projectContainer?.IsExpanded == true;

        var context = sessionVisible && sessionSelected
            ? "Session"
            : projectVisible && projectChildSelected
                ? "Ancestor"
                : startVisible && startSelected
                    ? "Start"
                    : "None";

        return string.Join(
            ";",
            $"Context={context}",
            $"Semantic={DescribeSemanticSelection(NavVM.CurrentSelection)}",
            $"NavSelected={DescribeNavSelection(MainNavView.SelectedItem)}",
            $"ProjectVisible={projectVisible}",
            $"ProjectSelected={projectSelected}",
            $"ProjectChildSelected={projectChildSelected}",
            $"ProjectExpanded={projectExpanded}",
            $"SessionVisible={sessionVisible}",
            $"SessionSelected={sessionSelected}",
            $"StartVisible={startVisible}",
            $"StartSelected={startSelected}");
    }

    private SessionNavItemViewModel? TryResolveCurrentSessionItem()
    {
        string? sessionId = null;
        if (NavVM.CurrentSelection is NavigationSelectionState.Session currentSession)
        {
            sessionId = currentSession.SessionId;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        // Try to find the session item in the navigation view model
        return NavVM.Items
            .OfType<ProjectNavItemViewModel>()
            .SelectMany(project => project.Children.OfType<SessionNavItemViewModel>())
            .FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.Ordinal));
    }

    private ProjectNavItemViewModel? TryResolveProjectItem(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        return NavVM.Items
            .OfType<ProjectNavItemViewModel>()
            .FirstOrDefault(project => string.Equals(project.ProjectId, projectId, StringComparison.Ordinal));
    }

    private static bool IsContainerVisible(Control? container)
        => container is not null
           && container.Visibility == Visibility.Visible
           && container.ActualWidth > 0
           && container.ActualHeight > 0;

    private static string ResolveResourceString(string resourceKey, string fallback)
    {
        var value = ResourceLoader.GetString(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

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

    private void OnLayoutViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => OnLayoutViewModelPropertyChanged(sender, e));
            return;
        }

        if (e.PropertyName == nameof(ShellLayoutViewModel.IsNavPaneOpen))
        {
            BootLogDebug($"LayoutVM IsNavPaneOpen Changed: mode={LayoutVM.NavPaneDisplayMode} open={LayoutVM.IsNavPaneOpen}");
            UpdateNavPaneToggleUi();
        }

        if (e.PropertyName == nameof(ShellLayoutViewModel.TitleBarInteractiveRegionToken))
        {
#if WINDOWS
            _titleBarAdapter.OnInteractiveRegionTokenChanged();
#endif
        }
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
        LayoutVM.PropertyChanged -= OnLayoutViewModelPropertyChanged;

        Loaded -= OnMainPageLoaded;
        Unloaded -= OnMainPageUnloaded;
        ContentFrame.Navigated -= OnContentFrameNavigated;
        ContentFrame.NavigationFailed -= OnContentFrameNavigationFailed;
    }
}
