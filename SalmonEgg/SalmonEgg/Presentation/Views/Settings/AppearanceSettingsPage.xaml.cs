using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class AppearanceSettingsPage : SalmonEgg.Presentation.Views.SettingsPageBase
{
    public AppPreferencesViewModel Preferences { get; }

    public AppearanceSettingsPage()
    {
        Preferences = App.ServiceProvider.GetRequiredService<AppPreferencesViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumb("外观");
    }
}
