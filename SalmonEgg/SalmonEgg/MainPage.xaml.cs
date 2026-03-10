using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;
using SalmonEgg.Presentation.Views.Chat;

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
    private bool _suppressNavSelectionChanged;
    private readonly Dictionary<object, NavigationViewItem> _navItemsByTag = new();
    private readonly HashSet<ObservableCollection<SessionNavItemViewModel>> _watchedSessionCollections = new();
    private readonly ShellPanePolicy _panePolicy = new();
#if WINDOWS
    private AppWindowTitleBar? _appWindowTitleBar;
#endif

    public AppPreferencesViewModel Preferences { get; }
    public SidebarViewModel SidebarVM { get; }
    public ChatViewModel ChatVM { get; }

    public MainPage()
    {
        App.BootLog("MainPage: ctor start");
        // 1. 在初始化组件前获取 ViewModel，确保 x:Bind 绑定正常
        Preferences = App.ServiceProvider.GetRequiredService<AppPreferencesViewModel>();
        SidebarVM = App.ServiceProvider.GetRequiredService<SidebarViewModel>();
        ChatVM = App.ServiceProvider.GetRequiredService<ChatViewModel>();

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

        // 4. 启动后默认进入对话界面
        NavigateToChat();
        App.BootLog("MainPage: navigated to ChatView");
    }

    private void OnMainPageUnloaded(object sender, RoutedEventArgs e)
    {
        SidebarVM.Projects.CollectionChanged -= OnProjectsCollectionChanged;
        SidebarVM.PropertyChanged -= OnSidebarPropertyChanged;

        foreach (var sessions in _watchedSessionCollections)
        {
            sessions.CollectionChanged -= OnSessionsCollectionChanged;
        }
        _watchedSessionCollections.Clear();
    }

    private sealed record NavTag(ProjectNavItemViewModel? Project = null, SessionNavItemViewModel? Session = null);

    private void ConfigureNavigationView()
    {
        if (MainNavView == null || ChatNavRoot == null || SettingsNavRoot == null)
        {
            return;
        }

        // Wire up sources that drive the navigation structure.
        SidebarVM.Projects.CollectionChanged += OnProjectsCollectionChanged;
        SidebarVM.PropertyChanged += OnSidebarPropertyChanged;

        // Map static settings items by Tag (string key).
        RegisterNavItemByTag(GeneralSettingsNavItem);
        RegisterNavItemByTag(AppearanceSettingsNavItem);
        RegisterNavItemByTag(AgentAcpSettingsNavItem);
        RegisterNavItemByTag(DataStorageSettingsNavItem);
        RegisterNavItemByTag(ShortcutsSettingsNavItem);
        RegisterNavItemByTag(DiagnosticsSettingsNavItem);
        RegisterNavItemByTag(AboutSettingsNavItem);

        RebuildChatProjectMenu();
    }

    private void RegisterNavItemByTag(NavigationViewItem? item)
    {
        if (item?.Tag is string key && !string.IsNullOrWhiteSpace(key))
        {
            _navItemsByTag[key] = item;
        }
    }

    private void OnProjectsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildChatProjectMenu();
    }

    private void OnSidebarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SidebarVM.SelectedProject))
        {
            SyncSelectedNavItemFromViewModel();
        }
    }

    private void RebuildChatProjectMenu()
    {
        if (ChatNavRoot == null)
        {
            return;
        }

        // Rebuild to keep the hierarchy in sync (projects + sessions are dynamic).
        ChatNavRoot.MenuItems.Clear();

        foreach (var sessions in _watchedSessionCollections)
        {
            sessions.CollectionChanged -= OnSessionsCollectionChanged;
        }
        _watchedSessionCollections.Clear();

        foreach (var project in SidebarVM.Projects)
        {
            if (project?.Sessions != null && _watchedSessionCollections.Add(project.Sessions))
            {
                project.Sessions.CollectionChanged += OnSessionsCollectionChanged;
            }

            var projectItem = new NavigationViewItem
            {
                Content = project.Name,
                Tag = new NavTag(Project: project),
                IsExpanded = project.IsExpanded,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Keep expand/collapse state in sync with the VM (used by other parts of the app).
            projectItem.SetBinding(NavigationViewItem.IsExpandedProperty, new Binding
            {
                Source = project,
                Path = new PropertyPath(nameof(ProjectNavItemViewModel.IsExpanded)),
                Mode = BindingMode.TwoWay
            });

            foreach (var session in project.Sessions)
            {
                var sessionItem = new NavigationViewItem
                {
                    Tag = new NavTag(Project: project, Session: session),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Keep session label reactive (rename from inline header or context menu should update immediately).
                sessionItem.SetBinding(ContentControl.ContentProperty, new Binding
                {
                    Source = session,
                    Path = new PropertyPath(nameof(SessionNavItemViewModel.Title)),
                    Mode = BindingMode.OneWay
                });

                sessionItem.ContextFlyout = BuildSessionContextFlyout(project, session);
                projectItem.MenuItems.Add(sessionItem);
            }

            ChatNavRoot.MenuItems.Add(projectItem);
        }

        SyncSelectedNavItemFromViewModel();
    }

    private MenuFlyout BuildSessionContextFlyout(ProjectNavItemViewModel project, SessionNavItemViewModel session)
    {
        var flyout = new MenuFlyout();

        var rename = new MenuFlyoutItem
        {
            Text = "重命名",
            Icon = new SymbolIcon(Symbol.Edit)
        };
        rename.Click += async (_, _) => await PromptRenameSessionAsync(session);

        var delete = new MenuFlyoutItem
        {
            Text = "删除",
            Icon = new SymbolIcon(Symbol.Delete)
        };
        delete.Click += async (_, _) => await PromptDeleteSessionAsync(project, session);

        flyout.Items.Add(rename);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(delete);

        return flyout;
    }

    private async Task PromptRenameSessionAsync(SessionNavItemViewModel session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.SessionId))
        {
            return;
        }

        var input = new TextBox
        {
            Text = session.Title ?? string.Empty,
            PlaceholderText = "会话名称",
            MinWidth = 320
        };

        var dialog = new ContentDialog
        {
            Title = "重命名会话",
            Content = input,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var sanitized = SessionNamePolicy.Sanitize(input.Text);
        var finalName = string.IsNullOrEmpty(sanitized)
            ? SessionNamePolicy.CreateDefault(session.SessionId)
            : sanitized;

        session.Title = finalName;
        ChatVM.RenameConversation(session.SessionId, finalName);
    }

    private async Task PromptDeleteSessionAsync(ProjectNavItemViewModel project, SessionNavItemViewModel session)
    {
        if (project == null || session == null || string.IsNullOrWhiteSpace(session.SessionId))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "删除会话？",
            Content = "此操作将删除该会话的本地记录（不可恢复）。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (project.SelectedSession == session)
        {
            project.SelectedSession = null;
        }

        project.Sessions.Remove(session);
        ChatVM.DeleteConversation(session.SessionId);
    }

    private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildChatProjectMenu();
    }

    private void SyncSelectedNavItemFromViewModel()
    {
        if (MainNavView == null || ChatNavRoot == null)
        {
            return;
        }

        var selectedProject = SidebarVM.SelectedProject;
        var selectedSession = selectedProject?.SelectedSession;

        NavigationViewItem? match = null;

        if (selectedSession != null)
        {
            match = FindNavItemByTag(MainNavView.MenuItems, t => t is NavTag tag && ReferenceEquals(tag.Session, selectedSession));
        }

        match ??= selectedProject != null
            ? FindNavItemByTag(MainNavView.MenuItems, t => t is NavTag tag && ReferenceEquals(tag.Project, selectedProject) && tag.Session == null)
            : null;

        if (match == null)
        {
            return;
        }

        _suppressNavSelectionChanged = true;
        try
        {
            MainNavView.SelectedItem = match;
        }
        finally
        {
            _suppressNavSelectionChanged = false;
        }
    }

    private static NavigationViewItem? FindNavItemByTag(IList<object> items, Func<object?, bool> predicate)
    {
        foreach (var obj in items)
        {
            if (obj is NavigationViewItem nvi)
            {
                if (predicate(nvi.Tag))
                {
                    return nvi;
                }

                if (nvi.MenuItems is { Count: > 0 })
                {
                    var found = FindNavItemByTag(nvi.MenuItems, predicate);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }

    public void NavigateToChat()
    {
        EnsureChatContent();

        if (MainNavView == null || ChatNavRoot == null)
        {
            return;
        }

        _suppressNavSelectionChanged = true;
        try
        {
            MainNavView.SelectedItem = ChatNavRoot;
        }
        finally
        {
            _suppressNavSelectionChanged = false;
        }
    }

    public void NavigateToSettingsSubPage(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            key = "General";
        }

        EnsureSettingsContent(key);

        if (MainNavView == null)
        {
            return;
        }

        if (!_navItemsByTag.TryGetValue(key, out var target))
        {
            target = GeneralSettingsNavItem;
        }

        if (target == null)
        {
            return;
        }

        _suppressNavSelectionChanged = true;
        try
        {
            MainNavView.SelectedItem = target;
        }
        finally
        {
            _suppressNavSelectionChanged = false;
        }
    }

    private static Type GetSettingsPageType(string key) => key switch
    {
        "General" => typeof(SalmonEgg.Presentation.Views.GeneralSettingsPage),
        "Appearance" => typeof(SalmonEgg.Presentation.Views.Settings.AppearanceSettingsPage),
        "AgentAcp" => typeof(SalmonEgg.Presentation.Views.Settings.AcpConnectionSettingsPage),
        "DataStorage" => typeof(SalmonEgg.Presentation.Views.Settings.DataStorageSettingsPage),
        "Shortcuts" => typeof(SalmonEgg.Presentation.Views.Settings.ShortcutsSettingsPage),
        "Diagnostics" => typeof(SalmonEgg.Presentation.Views.Settings.DiagnosticsSettingsPage),
        "About" => typeof(SalmonEgg.Presentation.Views.Settings.AboutPage),
        _ => typeof(SalmonEgg.Presentation.Views.GeneralSettingsPage)
    };

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

    private void OnMainNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavSelectionChanged || args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        if (item.Tag is NavTag tag && tag.Project != null)
        {
            SidebarVM.SelectedProject = tag.Project;
            if (tag.Session != null)
            {
                tag.Project.SelectedSession = tag.Session;
                _ = SidebarVM.TryActivateSessionAsync(tag.Session);
            }

            EnsureChatContent();
            return;
        }

        if (item.Tag is string key)
        {
            if (key == "Chat")
            {
                EnsureChatContent();
                return;
            }

            if (key == "Settings")
            {
                NavigateToSettingsSubPage("General");
                return;
            }

            if (_navItemsByTag.ContainsKey(key))
            {
                EnsureSettingsContent(key);
            }
        }
    }

    private void OnMainNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // SelectionChanged handles navigation; keep to avoid XAML event binding errors and for future use.
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

    private void EnsureSettingsContent(string key)
    {
        var pageType = GetSettingsPageType(key);
        if (ContentFrame?.CurrentSourcePageType != pageType)
        {
            NavigateTo(pageType);
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
            "Files" => "Files",
            _ => "Panel"
        };

        DiffPanel.Visibility = key == "Diff" ? Visibility.Visible : Visibility.Collapsed;
        TodoPanel.Visibility = key == "Todo" ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility = key == "Files" ? Visibility.Visible : Visibility.Collapsed;

        DiffPanelButton.IsChecked = key == "Diff";
        TodoPanelButton.IsChecked = key == "Todo";
        FilesPanelButton.IsChecked = key == "Files";
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
        FilesPanel.Visibility = Visibility.Collapsed;

        DiffPanelButton.IsChecked = false;
        TodoPanelButton.IsChecked = false;
        FilesPanelButton.IsChecked = false;
    }

    private void UpdateRightPanelAvailability(bool isChat)
    {
        DiffPanelButton.IsEnabled = isChat;
        TodoPanelButton.IsEnabled = isChat;
        FilesPanelButton.IsEnabled = isChat;

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
            // Prefer highlighting the currently-selected session/project if available.
            SyncSelectedNavItemFromViewModel();
            return;
        }

        var key = pageType == typeof(SalmonEgg.Presentation.Views.GeneralSettingsPage) ? "General"
            : pageType == typeof(SalmonEgg.Presentation.Views.Settings.AppearanceSettingsPage) ? "Appearance"
            : pageType == typeof(SalmonEgg.Presentation.Views.Settings.AcpConnectionSettingsPage) ? "AgentAcp"
            : pageType == typeof(SalmonEgg.Presentation.Views.Settings.DataStorageSettingsPage) ? "DataStorage"
            : pageType == typeof(SalmonEgg.Presentation.Views.Settings.ShortcutsSettingsPage) ? "Shortcuts"
            : pageType == typeof(SalmonEgg.Presentation.Views.Settings.DiagnosticsSettingsPage) ? "Diagnostics"
            : pageType == typeof(SalmonEgg.Presentation.Views.Settings.AboutPage) ? "About"
            : null;

        if (key == null || !_navItemsByTag.TryGetValue(key, out var target) || target == null)
        {
            return;
        }

        _suppressNavSelectionChanged = true;
        try
        {
            MainNavView.SelectedItem = target;
        }
        finally
        {
            _suppressNavSelectionChanged = false;
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
