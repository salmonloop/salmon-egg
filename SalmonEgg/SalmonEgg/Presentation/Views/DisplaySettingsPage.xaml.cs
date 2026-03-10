using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels;

namespace SalmonEgg.Presentation.Views
{
    public sealed partial class DisplaySettingsPage : SettingsPageBase
    {
        public SettingsViewModel SettingsVM { get; }

        public DisplaySettingsPage()
        {
            // 从全局 DI 容器获取 SettingsViewModel 以保持状态同步
            SettingsVM = App.ServiceProvider.GetRequiredService<SettingsViewModel>();

            this.InitializeComponent();
            SetSettingsBreadcrumb("显示与外观");
        }
    }
}
