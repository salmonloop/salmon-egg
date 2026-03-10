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
        SetSettingsBreadcrumb("快捷键");
    }

    private void OnRestoreSingleClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ShortcutEntryViewModel vm)
        {
            vm.Gesture = vm.DefaultGesture;
        }
    }
}
