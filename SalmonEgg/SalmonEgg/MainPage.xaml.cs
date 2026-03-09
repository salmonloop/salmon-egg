using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;
using SalmonEgg.Presentation.Views.Chat;

namespace SalmonEgg;

public sealed partial class MainPage : Page
{
    private const double SubMenuMinWidth = 200;
    private const double SubMenuMaxWidth = 420;
    private bool _isResizingSubMenu;
    private double _subMenuResizeStartX;
    private double _subMenuResizeStartWidth;

    public AppPreferencesViewModel Preferences { get; }
    public SidebarViewModel SidebarVM { get; }



    // 公开暴露导航列表，以便子页面可以触发全局导航切换
    public ListView MainRailNavList => MainRailNav;
    public ListView BottomRailNavList => BottomRailNav;

    public MainPage()
    {
        App.BootLog("MainPage: ctor start");
        // 1. 在初始化组件前获取 ViewModel，确保 x:Bind 绑定正常
        Preferences = App.ServiceProvider.GetRequiredService<AppPreferencesViewModel>();
        SidebarVM = App.ServiceProvider.GetRequiredService<SidebarViewModel>();

        this.InitializeComponent();
        App.BootLog("MainPage: InitializeComponent done");

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

        // 4. 初始化导航默认选中项（避免 XAML 初始化期间 SelectionChanged 触发导致 NRE）
        MainRailNav.SelectionChanged -= OnMainRailNavSelectionChanged;
        BottomRailNav.SelectionChanged -= OnBottomRailNavSelectionChanged;
        SettingsSubMenuList.SelectionChanged -= OnSubMenuSelectionChanged;
        try
        {
            MainRailNav.SelectedItem = ChatNavItem;
            BottomRailNav.SelectedIndex = -1;
            SubMenuColumn.Visibility = Visibility.Visible;

            ChatSubNavPanel.Visibility = Visibility.Visible;
            SettingsSubNavPanel.Visibility = Visibility.Collapsed;

            SettingsSubMenuList.SelectedIndex = -1;
        }
        finally
        {
            MainRailNav.SelectionChanged += OnMainRailNavSelectionChanged;
            BottomRailNav.SelectionChanged += OnBottomRailNavSelectionChanged;
            SettingsSubMenuList.SelectionChanged += OnSubMenuSelectionChanged;
        }

        // 5. 启动后默认进入对话界面
        NavigateTo(typeof(ChatView));
        App.BootLog("MainPage: navigated to ChatView");
    }

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

    private void OnMainRailNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BottomRailNav is null || SubMenuColumn is null || ContentFrame is null)
        {
            return;
        }

        if (MainRailNav.SelectedItem is ListViewItem item && item == ChatNavItem)
        {
            // 互斥逻辑：选中上方导航时，清除下方导航的选中状态
            BottomRailNav.SelectionChanged -= OnBottomRailNavSelectionChanged;
            BottomRailNav.SelectedIndex = -1;
            BottomRailNav.SelectionChanged += OnBottomRailNavSelectionChanged;

            // 聊天界面显示项目/会话子导航
            SubMenuColumn.Visibility = Visibility.Visible;
            ChatSubNavPanel.Visibility = Visibility.Visible;
            SettingsSubNavPanel.Visibility = Visibility.Collapsed;

            SettingsSubMenuList.SelectionChanged -= OnSubMenuSelectionChanged;
            SettingsSubMenuList.SelectedIndex = -1;
            SettingsSubMenuList.SelectionChanged += OnSubMenuSelectionChanged;
            NavigateTo(typeof(ChatView));
        }
    }

    private void OnBottomRailNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BottomRailNav.SelectedItem is ListViewItem item && item == SettingsNavItem)
        {
            // 互斥逻辑：选中下方导航时，清除上方导航的选中状态
            MainRailNav.SelectionChanged -= OnMainRailNavSelectionChanged;
            MainRailNav.SelectedIndex = -1;
            MainRailNav.SelectionChanged += OnMainRailNavSelectionChanged;

            // 展开二级导航栏（设置），并默认加载外观设置
            SubMenuColumn.Visibility = Visibility.Visible;
            ChatSubNavPanel.Visibility = Visibility.Collapsed;
            SettingsSubNavPanel.Visibility = Visibility.Visible;

            SettingsSubMenuList.SelectionChanged -= OnSubMenuSelectionChanged;
            SettingsSubMenuList.SelectedIndex = 1; // "外观"
            SettingsSubMenuList.SelectionChanged += OnSubMenuSelectionChanged;
            NavigateTo(typeof(SalmonEgg.Presentation.Views.Settings.AppearanceSettingsPage));
        }
    }

    // 处理二级导航的切换逻辑
    private void OnSubMenuSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ContentFrame is null || SubMenuColumn is null || SubMenuColumn.Visibility != Visibility.Visible || SettingsSubNavPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        if (sender is ListView listView && listView.SelectedItem is ListViewItem item)
        {
            string content = item.Content?.ToString() ?? "";

            if (content.Contains("常规"))
            {
                NavigateTo(typeof(SalmonEgg.Presentation.Views.GeneralSettingsPage));
                return;
            }

            if (content.Contains("外观"))
            {
                NavigateTo(typeof(SalmonEgg.Presentation.Views.Settings.AppearanceSettingsPage));
                return;
            }

            if (content.Contains("连接"))
            {
                NavigateTo(typeof(SalmonEgg.Presentation.Views.Settings.AcpConnectionSettingsPage));
                return;
            }

            if (content.Contains("数据"))
            {
                NavigateTo(typeof(SalmonEgg.Presentation.Views.Settings.DataStorageSettingsPage));
                return;
            }

            if (content.Contains("快捷键"))
            {
                NavigateTo(typeof(SalmonEgg.Presentation.Views.Settings.ShortcutsSettingsPage));
                return;
            }

            if (content.Contains("诊断"))
            {
                NavigateTo(typeof(SalmonEgg.Presentation.Views.Settings.DiagnosticsSettingsPage));
                return;
            }

            if (content.Contains("关于"))
            {
                NavigateTo(typeof(SalmonEgg.Presentation.Views.Settings.AboutPage));
                return;
            }
        }
    }

    private void OnSubMenuResizerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (SubMenuColumn is null || SubMenuResizer is null)
        {
            return;
        }

        _isResizingSubMenu = true;
        _subMenuResizeStartX = e.GetCurrentPoint(this).Position.X;
        _subMenuResizeStartWidth = SubMenuColumn.Width;

        SubMenuResizer.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnSubMenuResizerPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizingSubMenu || SubMenuColumn is null)
        {
            return;
        }

        var currentX = e.GetCurrentPoint(this).Position.X;
        var delta = currentX - _subMenuResizeStartX;

        var newWidth = _subMenuResizeStartWidth + delta;
        if (newWidth < SubMenuMinWidth)
        {
            newWidth = SubMenuMinWidth;
        }
        else if (newWidth > SubMenuMaxWidth)
        {
            newWidth = SubMenuMaxWidth;
        }

        SubMenuColumn.Width = newWidth;
        e.Handled = true;
    }

    private void OnSubMenuResizerPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndSubMenuResize(e.Pointer);
        e.Handled = true;
    }

    private void OnSubMenuResizerPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndSubMenuResize(e.Pointer);
    }

    private void EndSubMenuResize(Pointer pointer)
    {
        if (!_isResizingSubMenu || SubMenuResizer is null)
        {
            return;
        }

        _isResizingSubMenu = false;
        SubMenuResizer.ReleasePointerCapture(pointer);
    }
}
public sealed partial class MainPage
{
    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Preferences.PropertyChanged -= OnPreferencesPropertyChanged;
    }
}
