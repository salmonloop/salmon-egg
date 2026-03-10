using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class AboutPage : SettingsPageBase
{
    public AboutViewModel ViewModel { get; }

    public AboutPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<AboutViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumb("关于");
    }
}
