using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using UnoAcpClient.Presentation.ViewModels;
using UnoAcpClient.Presentation.Views;
using UnoAcpClient.Presentation.Views.Chat;

namespace UnoAcpClient;

public sealed partial class MainPage : Page
{
    public SettingsViewModel SettingsVM { get; }

    // 公开暴露导航列表，以便子页面可以触发全局导航切换
    public ListView MainRailNavList => MainRailNav;
    public ListView BottomRailNavList => BottomRailNav;

    public MainPage()
    {
        // 1. 在初始化组件前获取 ViewModel，确保 x:Bind 绑定正常
        SettingsVM = App.ServiceProvider.GetRequiredService<SettingsViewModel>();

        this.InitializeComponent();

#if !WINDOWS
        // Cross-platform fallback "Mica-like" backdrop.
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AppBackdropBrush", out var brush) && brush is Brush b)
        {
            Background = b;
        }
#endif

        // 2. 监听全局设置变化（如动画开关）
        SettingsVM.PropertyChanged += OnSettingsViewModelPropertyChanged;

        // 3. 初始化动画状态
        UpdateNavigationTransitions();

        // 4. 启动后默认进入对话界面
        NavigateTo(typeof(ChatView));
    }

    private void OnSettingsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsVM.IsAnimationEnabled))
        {
            UpdateNavigationTransitions();
        }
    }

    private void UpdateNavigationTransitions()
    {
        // 根据全局设置动态开启或关闭 Frame 的过渡动画
        if (SettingsVM.IsAnimationEnabled)
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
        var transition = SettingsVM.IsAnimationEnabled
            ? (NavigationTransitionInfo)new EntranceNavigationTransitionInfo()
            : new SuppressNavigationTransitionInfo();
        ContentFrame.Navigate(pageType, null, transition);
#else
        ContentFrame.Navigate(pageType);
#endif
    }

    private void OnMainRailNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainRailNav.SelectedItem is ListViewItem item && item == ChatNavItem)
        {
            // 互斥逻辑：选中上方导航时，清除下方导航的选中状态
            BottomRailNav.SelectionChanged -= OnBottomRailNavSelectionChanged;
            BottomRailNav.SelectedIndex = -1;
            BottomRailNav.SelectionChanged += OnBottomRailNavSelectionChanged;

            // 聊天界面不需要二级菜单
            SubMenuColumn.Visibility = Visibility.Collapsed;
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
            NavigateTo(typeof(DisplaySettingsPage));
        }
    }

    // 处理二级导航的切换逻辑
    private void OnSubMenuSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView listView && listView.SelectedItem is ListViewItem item)
        {
            string content = item.Content?.ToString() ?? "";

            if (content.Contains("外观") || content.Contains("常规"))
            {
                NavigateTo(typeof(DisplaySettingsPage));
            }
            else if (content.Contains("连接状态"))
            {
                // 连接配置已整合至 SettingsPage
                NavigateTo(typeof(SettingsPage));
            }
        }
    }
}
public sealed partial class MainPage
{
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        SettingsVM.PropertyChanged -= OnSettingsViewModelPropertyChanged;
    }
}
