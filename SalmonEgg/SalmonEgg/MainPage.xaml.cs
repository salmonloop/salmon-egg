using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Services.Shortcuts;
using SalmonEgg.Presentation.Core.ViewModels.Chat.TaskOverview;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Models.Search;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Shortcuts;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;
using SalmonEgg.Presentation.Views.Chat;
using SalmonEgg.Presentation.Views.Start;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg;

public sealed partial class MainPage : Page, INavigationIntentConsumer, IGamepadContextIntentConsumer
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();
    private const double NavPaneMinWidth = 240;
    private const double NavPaneMaxWidth = 480;
    private const double NavPaneAnimationDurationMs = 180;
    private const double RightPanelMinWidth = 240;
    private const double RightPanelMaxWidth = 520;
    private bool _isResizingRightPanel;
    private double _rightPanelResizeStartX;
    private double _rightPanelResizeStartWidth;
    private bool _isResizingLeftNav;
    private double _leftNavResizeStartX;
    private double _leftNavResizeStartWidth;

    private readonly DeferredActionGate<string> _archiveOnFlyoutClosed = new(StringComparer.Ordinal);
    private readonly DeferredActionGate<string> _moveOnFlyoutClosed = new(StringComparer.Ordinal);
    private readonly Dictionary<KeyboardAccelerator, string> _appShortcutActions = new();
    private string? _pendingArchiveSessionId;
    private string? _pendingMoveSessionId;
#if WINDOWS
    // Title bar hosting/interactive-region state is encapsulated by MainWindowTitleBarAdapter.
#endif

    public AppPreferencesViewModel Preferences { get; }
    public MainNavigationViewModel NavVM { get; }
    public GlobalSearchViewModel SearchVM { get; }
    public bool IsGuiAutomationMode { get; }
    private readonly ChatViewModel _chatViewModel;
    public ChatViewModel ChatVM => _chatViewModel;
    public ShellSessionActivationOverlayViewModel ShellOverlayVM { get; }
    public ShellLayoutViewModel LayoutVM { get; }
    private readonly WindowMetricsProvider _metricsProvider;
    private readonly AppActivationSignalSource _appActivationSignalSource;
    private readonly IShellLayoutMetricsSink _metricsSink;
    private readonly ILogger<MainPage> _logger;
    private readonly MainNavigationViewAdapter _mainNavigationViewAdapter;
    private readonly MainWindowTitleBarAdapter _titleBarAdapter;
    private readonly WindowBackdropService _windowBackdropService;
    private readonly IGamepadInputService _gamepadInputService;
    private readonly IGamepadNavigationDispatcher _gamepadNavigationDispatcher;
    private readonly IGamepadShortcutDispatcher _gamepadShortcutDispatcher;
    private readonly IGamepadContextIntentDispatcher _gamepadContextIntentDispatcher;
    private readonly IShellStartupNavigationService _startupNavigation;
    private readonly ContentFrameNavigationAdapter _contentNavigation;
    private bool _isGamepadInputAttached;
    private long _contentFrameNavigationVersion;

    public MainPage()
    {
        BootLogDebug("MainPage: ctor start");
        // 1. Get ViewModels before InitializeComponent to ensure x:Bind works correctly
        Preferences = App.ServiceProvider.GetRequiredService<AppPreferencesViewModel>();
        NavVM = App.ServiceProvider.GetRequiredService<MainNavigationViewModel>();
        _chatViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();
        ShellOverlayVM = App.ServiceProvider.GetRequiredService<ShellSessionActivationOverlayViewModel>();
        SearchVM = App.ServiceProvider.GetRequiredService<GlobalSearchViewModel>();

        LayoutVM = App.ServiceProvider.GetRequiredService<ShellLayoutViewModel>();
        _metricsProvider = App.ServiceProvider.GetRequiredService<WindowMetricsProvider>();
        _appActivationSignalSource = App.ServiceProvider.GetRequiredService<AppActivationSignalSource>();
        _metricsSink = App.ServiceProvider.GetRequiredService<IShellLayoutMetricsSink>();
        var navigationCoordinator = App.ServiceProvider.GetRequiredService<INavigationCoordinator>();
        _logger = App.ServiceProvider.GetRequiredService<ILogger<MainPage>>();
        _windowBackdropService = App.ServiceProvider.GetRequiredService<WindowBackdropService>();
        _gamepadInputService = App.ServiceProvider.GetRequiredService<IGamepadInputService>();
        _gamepadNavigationDispatcher = App.ServiceProvider.GetRequiredService<IGamepadNavigationDispatcher>();
        _gamepadShortcutDispatcher = App.ServiceProvider.GetRequiredService<IGamepadShortcutDispatcher>();
        _gamepadContextIntentDispatcher = App.ServiceProvider.GetRequiredService<IGamepadContextIntentDispatcher>();
        _startupNavigation = App.ServiceProvider.GetRequiredService<IShellStartupNavigationService>();
        IsGuiAutomationMode = string.Equals(
            Environment.GetEnvironmentVariable("SALMONEGG_GUI"),
            "1",
            StringComparison.Ordinal);

        this.InitializeComponent();
        _contentNavigation = new ContentFrameNavigationAdapter(ContentFrame);
        _mainNavigationViewAdapter = new MainNavigationViewAdapter(
            NavVM,
            navigationCoordinator);
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
            navigationCoordinator,
            _logger);
        BootLogDebug("MainPage: InitializeComponent done");

        Loaded += OnMainPageLoaded;
        Unloaded += OnMainPageUnloaded;
        _contentNavigation.NavigationCompleted += OnContentFrameNavigationCompleted;
        _contentNavigation.NavigationFailed += OnContentFrameNavigationFailed;

        // 2. Listen for global preference changes (animations, theme, backdrop)
        Preferences.PropertyChanged += OnPreferencesPropertyChanged;
        Preferences.ShortcutBindingsChanged += OnShortcutBindingsChanged;
        NavVM.PropertyChanged += OnNavigationViewModelPropertyChanged;
        NavVM.TreeRebuilt += OnNavigationTreeRebuilt;
        LayoutVM.PropertyChanged += OnLayoutViewModelPropertyChanged;

        // 3. Initialize theme and motion state
        ApplyTheme();
        ApplyBackdrop();
        RebuildAppShortcuts();
        // NavVM.PropertyChanged registration removed as layout is now driven by LayoutVM SSOT
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

    partial void AttachPlatformGamepadDirectionalBridge();

    partial void DetachPlatformGamepadDirectionalBridge();

    private bool ShouldSuppressPolledGamepadIntent(GamepadNavigationIntent intent)
    {
#if WINDOWS
        return ShouldSuppressPolledGamepadIntentForWindows(intent);
#else
        return false;
#endif
    }

    private bool ShouldSuppressPolledGamepadShortcut(GamepadShortcutIntent intent)
    {
#if WINDOWS
        return ShouldSuppressPolledGamepadShortcutForWindows(intent);
#else
        return false;
#endif
    }

    private bool ShouldSuppressPolledGamepadContextIntent(GamepadContextIntent intent)
    {
#if WINDOWS
        return ShouldSuppressPolledGamepadContextIntentForWindows(intent);
#else
        return false;
#endif
    }

    private void OnMainPageUnloaded(object sender, RoutedEventArgs e)
    {
        DetachGamepadInput();
        DetachPlatformGamepadDirectionalBridge();
        DetachDebugKeyLogging();
        Preferences.PropertyChanged -= OnPreferencesPropertyChanged;
        Preferences.ShortcutBindingsChanged -= OnShortcutBindingsChanged;
        NavVM.PropertyChanged -= OnNavigationViewModelPropertyChanged;
        NavVM.TreeRebuilt -= OnNavigationTreeRebuilt;
        LayoutVM.PropertyChanged -= OnLayoutViewModelPropertyChanged;
        _metricsProvider.Detach();
        _contentNavigation.NavigationCompleted -= OnContentFrameNavigationCompleted;
        _contentNavigation.NavigationFailed -= OnContentFrameNavigationFailed;
        _titleBarAdapter.Detach();
        DisposePlatformTray();
    }

    public void NavigateToChat()
    {
        _ = EnsureChatContentAsync();
    }

    public ValueTask<ShellNavigationResult> NavigateToChatAsync()
        => EnsureChatContentAsync();

    public ValueTask<ShellNavigationResult> NavigateToChatAsync(long activationToken)
        => EnsureChatContentAsync(activationToken);

    public void NavigateToStart()
    {
        _ = EnsureStartContentAsync();
    }

    public ValueTask<ShellNavigationResult> NavigateToStartAsync()
        => EnsureStartContentAsync();

    public ValueTask<ShellNavigationResult> NavigateToStartAsync(long activationToken)
        => EnsureStartContentAsync(activationToken);

    public void NavigateToDiscoverSessions()
    {
        _ = EnsureDiscoverSessionsContentAsync();
    }

    public ValueTask<ShellNavigationResult> NavigateToDiscoverSessionsAsync()
        => EnsureDiscoverSessionsContentAsync();

    public ValueTask<ShellNavigationResult> NavigateToDiscoverSessionsAsync(long activationToken)
        => EnsureDiscoverSessionsContentAsync(activationToken);

    public void NavigateToSettingsSubPage(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            key = SettingsSectionCatalog.GeneralKey;
        }

        _ = EnsureSettingsContentAsync(key);
    }

    public ValueTask<ShellNavigationResult> NavigateToSettingsSubPageAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            key = SettingsSectionCatalog.GeneralKey;
        }

        return EnsureSettingsContentAsync(key);
    }

    public ValueTask<ShellNavigationResult> NavigateToSettingsSubPageAsync(string key, long activationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            key = SettingsSectionCatalog.GeneralKey;
        }

        return EnsureSettingsContentAsync(key, activationToken);
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

        if (e.PropertyName == nameof(Preferences.MinimizeToTray))
        {
            UpdateTrayState();
        }
    }

    private void OnShortcutBindingsChanged(object? sender, EventArgs e)
    {
        RebuildAppShortcuts();
    }

    private void RebuildAppShortcuts()
    {
        foreach (var accelerator in _appShortcutActions.Keys)
        {
            accelerator.Invoked -= OnAppShortcutInvoked;
        }

        KeyboardAccelerators.Clear();
        _appShortcutActions.Clear();

        var savedBindings = Preferences.KeyBindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.ActionId) && !string.IsNullOrWhiteSpace(binding.Gesture))
            .ToDictionary(binding => binding.ActionId, binding => binding.Gesture, StringComparer.OrdinalIgnoreCase);

        var bindingMap = AppShortcutBindingMap.Create(savedBindings);
        foreach (var binding in bindingMap.AsDictionary())
        {
            if (!WinUiAppShortcutProjector.TryProject(binding.Key, out var key, out var modifiers))
            {
                continue;
            }

            var accelerator = new KeyboardAccelerator
            {
                Key = key,
                Modifiers = modifiers
            };
            accelerator.Invoked += OnAppShortcutInvoked;
            KeyboardAccelerators.Add(accelerator);
            _appShortcutActions[accelerator] = binding.Value;
        }
    }

    private async void OnAppShortcutInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_appShortcutActions.TryGetValue(sender, out var actionId))
        {
            return;
        }

        switch (actionId)
        {
            case AppShortcutActionIds.NewSession:
                await NavVM.PrepareStartForProjectAsync(MainNavigationViewModel.UnclassifiedProjectId).ConfigureAwait(true);
                args.Handled = true;
                return;
            case AppShortcutActionIds.Search:
                FocusTopSearchBox();
                args.Handled = true;
                return;
        }
    }

    private void FocusTopSearchBox()
    {
        if (!LayoutVM.SearchBoxVisible)
        {
            return;
        }

        _ = TopSearchBox.Focus(FocusState.Keyboard);
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
        // Re-attach the main window when backdrop preference changes so the shared
        // window service remains the SSOT but the main shell still has a recovery path.
        var window = App.MainWindowInstance;
        if (window != null)
        {
            _windowBackdropService.Attach(window);
        }

        return;
#else
        // Cross-platform fallback "Mica-like" backdrop.
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AppBackdropBrush", out var brush) && brush is Brush b)
        {
            Background = b;
        }
#endif
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

    private async ValueTask<ShellNavigationResult> EnsureChatContentAsync(long? activationToken = null)
    {
        var result = await NavigateToContentAsync(typeof(ChatView), activationToken: activationToken).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            var reason = result.FailureReason ?? "Unknown";
            BootLogDebug($"Content navigation rejected: target=ChatView reason={reason} token={activationToken?.ToString() ?? "<null>"}");
            _logger.LogWarning(
                "Shell content navigation rejected. target={Target} reason={Reason} activationToken={ActivationToken}",
                nameof(ChatView),
                reason,
                activationToken);
        }

        _titleBarAdapter.UpdateBackButtonState();
        return result;
    }

    private async ValueTask<ShellNavigationResult> EnsureDiscoverSessionsContentAsync(long? activationToken = null)
    {
        var pageType = typeof(SalmonEgg.Presentation.Views.Discover.DiscoverSessionsPage);
        var result = await NavigateToContentAsync(pageType, activationToken: activationToken).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            var reason = result.FailureReason ?? "Unknown";
            BootLogDebug($"Content navigation rejected: target={pageType.Name} reason={reason} token={activationToken?.ToString() ?? "<null>"}");
            _logger.LogWarning(
                "Shell content navigation rejected. target={Target} reason={Reason} activationToken={ActivationToken}",
                pageType.Name,
                reason,
                activationToken);
        }

        _titleBarAdapter.UpdateBackButtonState();
        return result;
    }

    private async ValueTask<ShellNavigationResult> EnsureStartContentAsync(long? activationToken = null)
    {
        var result = await NavigateToContentAsync(typeof(StartView), activationToken: activationToken).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            var reason = result.FailureReason ?? "Unknown";
            BootLogDebug($"Content navigation rejected: target=StartView reason={reason} token={activationToken?.ToString() ?? "<null>"}");
            _logger.LogWarning(
                "Shell content navigation rejected. target={Target} reason={Reason} activationToken={ActivationToken}",
                nameof(StartView),
                reason,
                activationToken);
        }

        _titleBarAdapter.UpdateBackButtonState();
        return result;
    }

    private async ValueTask<ShellNavigationResult> EnsureSettingsContentAsync(string key, long? activationToken = null)
    {
        var pageType = GetSettingsShellPageType();
        var result = await NavigateToContentAsync(pageType, key, activationToken).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            var reason = result.FailureReason ?? "Unknown";
            BootLogDebug($"Content navigation rejected: target={pageType.Name} reason={reason} token={activationToken?.ToString() ?? "<null>"}");
            _logger.LogWarning(
                "Shell content navigation rejected. target={Target} reason={Reason} activationToken={ActivationToken}",
                pageType.Name,
                reason,
                activationToken);
        }

        if (result.Succeeded)
        {
            (ContentFrame.Content as SalmonEgg.Presentation.Views.SettingsShellPage)?.NavigateToSection(key);
        }

        _titleBarAdapter.UpdateBackButtonState();
        return result;
    }

    private ValueTask<ShellNavigationResult> NavigateToContentAsync(
        Type pageType,
        object? parameter = null,
        long? activationToken = null)
        => _contentNavigation.NavigateAsync(pageType, parameter, activationToken);

    private string GetRightPanelTitle(RightPanelMode mode)
    {
        return mode switch
        {
            RightPanelMode.TaskOverview => ResolveResourceString("TaskOverviewPanelTitle.Text", "Task overview"),
            _ => string.Empty
        };
    }

    private string GetTaskOverviewSummaryText(TaskOverviewPanelState state)
        => string.Format(
            CultureInfo.CurrentCulture,
            ResolveResourceString("TaskOverviewSummaryFormat.Text", "{0} in progress · {1} pending · {2} completed · {3} files changed"),
            state.ActivePlanCount,
            state.PendingPlanCount,
            state.CompletedPlanCount,
            state.ChangeCount);

    private string GetTaskOverviewSummaryAutomationName(TaskOverviewPanelState state)
        => string.Format(
            CultureInfo.CurrentCulture,
            ResolveResourceString(
                "TaskOverviewSummaryAutomationFormat.Text",
                "Task overview summary: {0} in progress, {1} pending, {2} completed, {3} files changed"),
            state.ActivePlanCount,
            state.PendingPlanCount,
            state.CompletedPlanCount,
            state.ChangeCount);

    private string GetTaskOverviewMorePlanText(int hiddenCount)
        => string.Format(
            CultureInfo.CurrentCulture,
            ResolveResourceString("TaskOverviewMorePlanItemsFormat.Text", "{0} more plan items"),
            hiddenCount);

    private string GetTaskOverviewMoreChangesText(int hiddenCount)
        => string.Format(
            CultureInfo.CurrentCulture,
            ResolveResourceString("TaskOverviewMoreChangesFormat.Text", "{0} more changed files"),
            hiddenCount);

    private string GetTaskOverviewCurrentPlanAutomationName(
        string content,
        PlanEntryStatus? status,
        PlanEntryPriority? priority)
    {
        var statusText = ResolveTaskOverviewStatusLabel(status);
        var priorityText = ResolveTaskOverviewPriorityLabel(priority);
        return string.Format(
            CultureInfo.CurrentCulture,
            ResolveResourceString("TaskOverviewCurrentPlanAutomationFormat.Text", "Current plan item: {0}. Status: {1}. Priority: {2}."),
            content,
            statusText,
            priorityText);
    }

    private string ResolveTaskOverviewStatusLabel(PlanEntryStatus? status)
        => status switch
        {
            PlanEntryStatus.Pending => ResolveResourceString("TaskOverviewPlanStatusPending.Text", "Pending"),
            PlanEntryStatus.InProgress => ResolveResourceString("TaskOverviewPlanStatusInProgress.Text", "In progress"),
            PlanEntryStatus.Completed => ResolveResourceString("TaskOverviewPlanStatusCompleted.Text", "Completed"),
            _ => ResolveResourceString("TaskOverviewPlanStatusUnknown.Text", "Unknown")
        };

    private string ResolveTaskOverviewPriorityLabel(PlanEntryPriority? priority)
        => priority switch
        {
            PlanEntryPriority.Low => ResolveResourceString("TaskOverviewPlanPriorityLow.Text", "Low"),
            PlanEntryPriority.Medium => ResolveResourceString("TaskOverviewPlanPriorityMedium.Text", "Medium"),
            PlanEntryPriority.High => ResolveResourceString("TaskOverviewPlanPriorityHigh.Text", "High"),
            _ => ResolveResourceString("TaskOverviewPlanPriorityUnknown.Text", "Unknown")
        };

    private DataTemplate GetBottomPanelButtonIconTemplate(BottomPanelMode mode)
        => GetAuxiliaryIconTemplate(mode == BottomPanelMode.Dock
            ? "BottomPanelTitleBarFilledIconTemplate"
            : "BottomPanelTitleBarRegularIconTemplate");

    private DataTemplate GetTaskOverviewPanelButtonIconTemplate(RightPanelMode mode)
        => GetAuxiliaryIconTemplate(mode == RightPanelMode.TaskOverview
            ? "TaskOverviewPanelTitleBarFilledIconTemplate"
            : "TaskOverviewPanelTitleBarRegularIconTemplate");

    private string GetBottomPanelButtonToolTip(bool canToggle)
        => GetAuxiliaryPanelButtonToolTip(
            canToggle,
            "BottomPanelButton.ToolTipService.ToolTip",
            "Bottom Panel");

    private string GetTaskOverviewPanelButtonToolTip(bool canToggle)
        => GetAuxiliaryPanelButtonToolTip(
            canToggle,
            "TaskOverviewPanelButton.ToolTipService.ToolTip",
            "Task overview");

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
        if (RightPanelPane is null || RightPanelResizer is null || LayoutVM.RightPanelVisible == false)
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
        if (!_isResizingRightPanel || RightPanelPane is null)
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
        AttachPlatformGamepadDirectionalBridge();
        AttachDebugKeyLogging();
        _titleBarAdapter.Configure(App.MainWindowInstance);
        _appActivationSignalSource.Attach(App.MainWindowInstance!);
        _metricsProvider.Attach(App.MainWindowInstance!, _titleBarAdapter);
        UpdateNavPaneToggleUi();
        NavVM.RebuildTree();
        UpdateMainNavAutomationSelectionState();
        await _startupNavigation.ActivateInitialContentAsync().ConfigureAwait(true);
        BootLogDebug("MainPage: initial shell content activated");
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                _ = TryMoveFocusFromCurrentContentIntoMainNavigation();
            });
        InitializeTray();
        await _chatViewModel.RestoreAsync();
    }

    private void AttachGamepadInput()
    {
        if (_isGamepadInputAttached)
        {
            return;
        }

        _gamepadInputService.IntentRaised += OnGamepadIntentRaised;
        _gamepadInputService.ShortcutRaised += OnGamepadShortcutRaised;
        _gamepadInputService.ContextIntentRaised += OnGamepadContextIntentRaised;
        _gamepadInputService.Start();
        _logger.LogDebug("Gamepad input service attached to shell handlers.");
        _isGamepadInputAttached = true;
    }

    private void DetachGamepadInput()
    {
        if (!_isGamepadInputAttached)
        {
            return;
        }

        _gamepadInputService.IntentRaised -= OnGamepadIntentRaised;
        _gamepadInputService.ShortcutRaised -= OnGamepadShortcutRaised;
        _gamepadInputService.ContextIntentRaised -= OnGamepadContextIntentRaised;
        _gamepadInputService.Stop();
        _logger.LogDebug("Gamepad input service detached from shell handlers.");
        _isGamepadInputAttached = false;
    }

    private void OnGamepadIntentRaised(object? sender, GamepadNavigationIntent intent)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _logger.LogDebug(
                "Gamepad navigation intent received on non-UI thread, dispatching to UI. Intent={Intent}.",
                intent);
            _ = DispatcherQueue.TryEnqueue(() => OnGamepadIntentRaised(sender, intent));
            return;
        }

        if (ShouldSuppressPolledGamepadIntent(intent))
        {
            _logger.LogDebug(
                "Gamepad navigation intent suppressed due duplicate native keydown. Intent={Intent}.",
                intent);
            return;
        }

        var consumed = _gamepadNavigationDispatcher.TryDispatch(intent);
        _logger.LogDebug(
            "Gamepad navigation intent dispatched from poller. Intent={Intent} Consumed={Consumed}.",
            intent,
            consumed);
    }

    private void OnGamepadShortcutRaised(object? sender, GamepadShortcutIntent intent)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _logger.LogDebug(
                "Gamepad shortcut intent received on non-UI thread, dispatching to UI. Intent={Intent}.",
                intent);
            _ = DispatcherQueue.TryEnqueue(() => OnGamepadShortcutRaised(sender, intent));
            return;
        }

        if (ShouldSuppressPolledGamepadShortcut(intent))
        {
            _logger.LogDebug(
                "Gamepad shortcut intent suppressed due duplicate native keydown. Intent={Intent}.",
                intent);
            return;
        }

        var consumed = _gamepadShortcutDispatcher.TryDispatch(intent);
        _logger.LogDebug(
            "Gamepad shortcut intent dispatched from poller. Intent={Intent} Consumed={Consumed}.",
            intent,
            consumed);
    }

    private void OnGamepadContextIntentRaised(object? sender, GamepadContextIntent intent)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _logger.LogDebug(
                "Gamepad context intent received on non-UI thread, dispatching to UI. Intent={Intent}.",
                intent);
            _ = DispatcherQueue.TryEnqueue(() => OnGamepadContextIntentRaised(sender, intent));
            return;
        }

        if (ShouldSuppressPolledGamepadContextIntent(intent))
        {
            _logger.LogDebug(
                "Gamepad context intent suppressed due duplicate native keydown. Intent={Intent}.",
                intent);
            return;
        }

        var consumed = _gamepadContextIntentDispatcher.TryDispatch(intent);
        _logger.LogDebug(
            "Gamepad context intent dispatched from poller. Intent={Intent} Consumed={Consumed}.",
            intent,
            consumed);
    }

    public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        if (intent != GamepadNavigationIntent.MoveRight)
        {
            return false;
        }

        if (!IsFocusWithinMainNavigation())
        {
            return false;
        }

        return TryMoveFocusFromMainNavigationIntoCurrentContent();
    }

    public bool TryConsumeContextIntent(GamepadContextIntent intent)
    {
        if (ContentFrame.Content is IGamepadContextIntentConsumer contentConsumer
            && contentConsumer.TryConsumeContextIntent(intent))
        {
            return true;
        }

        return false;
    }

    public bool TryGoBack()
    {
        return _titleBarAdapter.TryGoBack();
    }

    public bool TryHandleGamepadBack()
    {
        // Product-specific gamepad Back semantics:
        // 1. Let the currently focused page/content consume a local back action first.
        // 2. If the user is in page content, return focus to the authoritative left-nav target.
        // 3. Only then fall back to the shell/page back owner.
        if (TryConsumeFocusedNavigationIntent(GamepadNavigationIntent.Back))
        {
            return true;
        }

        if (TryMoveFocusFromCurrentContentIntoMainNavigation())
        {
            return true;
        }

        return TryGoBack();
    }

    private bool TryConsumeFocusedNavigationIntent(GamepadNavigationIntent intent)
    {
        var root = App.MainWindowInstance?.Content as FrameworkElement;
        if (root?.XamlRoot is null)
        {
            return false;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
        while (current is not null)
        {
            if (current is INavigationIntentConsumer consumer
                && consumer.TryConsumeNavigationIntent(intent))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool IsFocusWithinMainNavigation()
    {
        if (MainNavView.XamlRoot is null)
        {
            return false;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(MainNavView.XamlRoot) as DependencyObject;
        if (IsDescendantOf(current, ContentFrame))
        {
            return false;
        }

        while (current is not null)
        {
            if (ReferenceEquals(current, MainNavView))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool IsDescendantOf(DependencyObject? current, DependencyObject target)
    {
        while (current is not null)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool TryMoveFocusFromMainNavigationIntoCurrentContent()
    {
        if (ContentFrame.Content is IPrimaryContentFocusTarget focusTarget)
        {
            return focusTarget.TryFocusPrimaryContentTarget();
        }

        if (ContentFrame.Content is FrameworkElement element)
        {
            return element.Focus(FocusState.Programmatic);
        }

        return false;
    }

    private bool TryConsumeCurrentPageNavigationIntent(GamepadNavigationIntent intent)
    {
        if (ContentFrame.Content is INavigationIntentConsumer pageConsumer
            && pageConsumer.TryConsumeNavigationIntent(intent))
        {
            return true;
        }

        return false;
    }

    private bool TryMoveFocusFromCurrentContentIntoMainNavigation()
    {
        if (MainNavView.XamlRoot is null || IsFocusWithinMainNavigation())
        {
            return false;
        }

        var navigationTarget = ResolveCurrentNavigationFocusTarget();
        if (navigationTarget is not null
            && MainNavView.ContainerFromMenuItem(navigationTarget) is Control selectedContainer
            && selectedContainer.Focus(FocusState.Programmatic))
        {
            return true;
        }

        return MainNavView.Focus(FocusState.Programmatic);
    }

    private object? ResolveCurrentNavigationFocusTarget()
    {
        if (NavVM.ProjectedControlSelectedItem is not null)
        {
            return NavVM.ProjectedControlSelectedItem;
        }

        if (NavVM.IsSettingsSelected)
        {
            return NavVM.SettingsItem;
        }

        if (NavVM.CurrentSelection == NavigationSelectionState.StartSelection)
        {
            return NavVM.StartItem;
        }

        if (NavVM.CurrentSelection == NavigationSelectionState.DiscoverSessionsSelection)
        {
            return NavVM.DiscoverSessionsItem;
        }

        return MainNavView.SelectedItem;
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

    private async void OnContentFrameNavigationCompleted(object? sender, ContentFrameNavigationCompletedEventArgs e)
    {
        BootLogDebug($"ContentFrame Navigated: {e.PageType.Name}");
        var navigationVersion = Interlocked.Increment(ref _contentFrameNavigationVersion);
        var isChatPage = IsChatPageType(e.PageType);
        try
        {
            await _metricsSink.ReportContentContext(isChatPage, navigationVersion).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content frame navigation projection failed. IsChatPage={IsChatPage}", isChatPage);
        }

        _titleBarAdapter.UpdateBackButtonState();
    }

    private void OnContentFrameNavigationFailed(object? sender, ContentFrameNavigationFailedEventArgs e)
    {
        BootLogDebug($"ContentFrame NavigationFailed: target={e.PageType.Name} reason={e.Reason}");
    }

    private void OnTitleBarBackClick(object sender, RoutedEventArgs e)
    {
        TryGoBack();
    }

    private async void OnToggleLeftNavClick(object sender, RoutedEventArgs e)
    {
        BootLogDebug($"TitleBar ToggleSidebar Clicked: mode={LayoutVM.NavPaneDisplayMode} open={LayoutVM.IsNavPaneOpen}");
        await _metricsSink.ReportNavToggle("TitleBarButton");
    }

    private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is SearchSuggestionEntry entry)
        {
            await SearchVM.ActivateSuggestionAsync(entry).ConfigureAwait(true);
            return;
        }

        await SearchVM.SubmitQueryAsync(args.QueryText).ConfigureAwait(true);
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput
            && !string.Equals(SearchVM.Query, sender.Text, StringComparison.Ordinal))
        {
            SearchVM.Query = sender.Text;
        }
    }

    private void OnSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not SearchSuggestionEntry entry)
        {
            return;
        }

        sender.Text = entry.HistoryQuery ?? entry.Title;
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
        if (string.IsNullOrWhiteSpace(value))
        {
            value = ResourceLoader.GetString(resourceKey.Replace('.', '/'));
        }

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

    partial void InitializeTray();

    partial void UpdateTrayState();

    partial void DisposePlatformTray();

    partial void AttachDebugKeyLogging();

    partial void DetachDebugKeyLogging();

    // TitleBar insets are now handled by WindowMetricsProvider reporting to IShellLayoutMetricsSink,
    // which updates ShellLayoutStore/ShellLayoutViewModel. Visuals are bound using x:Bind in XAML.
}
public sealed partial class MainPage
{
    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Preferences.PropertyChanged -= OnPreferencesPropertyChanged;
        LayoutVM.PropertyChanged -= OnLayoutViewModelPropertyChanged;

        Loaded -= OnMainPageLoaded;
        Unloaded -= OnMainPageUnloaded;
        _contentNavigation.NavigationCompleted -= OnContentFrameNavigationCompleted;
        _contentNavigation.NavigationFailed -= OnContentFrameNavigationFailed;
    }
}
