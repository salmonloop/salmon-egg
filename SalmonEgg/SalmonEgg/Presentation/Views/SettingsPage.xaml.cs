using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }
        public ChatViewModel ChatViewModel { get; }

        public SettingsPage()
        {
            // 从全局 DI 容器获取 ViewModel 以保持状态同步
            ViewModel = App.ServiceProvider.GetRequiredService<SettingsViewModel>();
            ChatViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();

            this.InitializeComponent();

            this.Loaded += SettingsPage_Loaded;
        }

        private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 加载已保存的配置列表
            await ViewModel.LoadConfigurationsAsync();
        }

        private void OnTransportTypeChanged(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton && radioButton.CommandParameter is string transportTypeStr)
            {
                var transportType = transportTypeStr switch
                {
                    "Stdio" => TransportType.Stdio,
                    "WebSocket" => TransportType.WebSocket,
                    "HttpSse" => TransportType.HttpSse,
                    _ => TransportType.Stdio
                };

                if (ChatViewModel?.TransportConfig != null)
                {
                    ChatViewModel.TransportConfig.SelectedTransportType = transportType;
                }
            }
        }

        private void EditConfiguration_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ServerConfiguration config)
            {
                ViewModel.EditConfiguration(config);
            }
        }

        private async void DeleteConfiguration_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ServerConfiguration config)
            {
                await ViewModel.DeleteConfigurationAsync(config);
            }
        }

        private void OnGoToChatClick(object sender, RoutedEventArgs e)
        {
            DependencyObject? current = this;
            while (current != null && current is not MainPage)
            {
                current = VisualTreeHelper.GetParent(current);
            }

            if (current is MainPage mainPage)
            {
                mainPage.MainRailNavList.SelectedIndex = 0;
            }
        }
    }
}
