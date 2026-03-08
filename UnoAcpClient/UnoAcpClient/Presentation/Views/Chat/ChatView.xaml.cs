using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using UnoAcpClient.Presentation.ViewModels.Chat;

namespace UnoAcpClient.Presentation.Views.Chat
{
    public sealed partial class ChatView : Page
    {
        public ChatViewModel ViewModel { get; }

        public ChatView()
        {
            // 从全局服务容器获取 ViewModel 以确保状态在导航间持久化
            ViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();

            this.InitializeComponent();
        }

        private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // 支持 Ctrl+Enter 发送消息
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                if (ctrlPressed)
                {
                    // 在 ChatViewModel 中，发送命令是 SendPromptCommand
                    if (ViewModel.SendPromptCommand != null && ViewModel.SendPromptCommand.CanExecute(null))
                    {
                        ViewModel.SendPromptCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        }

        private void OnGoToSettingsClick(object sender, RoutedEventArgs e)
        {
            // 通过视觉树向上查找 MainPage 实例以触发侧边栏切换
            DependencyObject? current = this;
            while (current != null && !(current is MainPage))
            {
                current = VisualTreeHelper.GetParent(current);
            }

            if (current is MainPage mainPage)
            {
                // 切换到底部导航栏的“设置”项
                // 使用公开的 BottomRailNavList 属性以避免权限问题
                mainPage.BottomRailNavList.SelectedIndex = 0;
            }
        }
    }
}
