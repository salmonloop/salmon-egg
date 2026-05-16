using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Views
{
public sealed partial class GeneralSettingsPage : SettingsPageBase
{
    public GeneralSettingsViewModel ViewModel { get; }

    public GeneralSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<GeneralSettingsViewModel>();
        this.InitializeComponent();
        SetSettingsBreadcrumbFromResource("SettingsNav_General.Content", "常规");
    }
}
}
