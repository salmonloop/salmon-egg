using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Views
{
    public sealed partial class GeneralSettingsPage : SettingsPageBase
    {
        public AppPreferencesViewModel Preferences { get; }

        public GeneralSettingsPage()
        {
            Preferences = App.ServiceProvider.GetRequiredService<AppPreferencesViewModel>();
            this.InitializeComponent();
            SetSettingsBreadcrumb("常规");
        }
    }
}
