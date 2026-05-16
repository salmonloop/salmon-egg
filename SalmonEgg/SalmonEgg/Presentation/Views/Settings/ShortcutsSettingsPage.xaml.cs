using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class ShortcutsSettingsPage : SettingsPageBase
{
    public ShortcutsSettingsViewModel ViewModel { get; }

    public ShortcutsSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<ShortcutsSettingsViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumbFromResource("SettingsNav_Shortcuts.Content", "快捷键");
    }
}
