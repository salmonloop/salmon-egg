using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class ShortcutsSettingsPage : Page
{
    public ShortcutsSettingsViewModel ViewModel { get; }

    public ShortcutsSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<ShortcutsSettingsViewModel>();
        InitializeComponent();
    }

    private void OnRestoreSingleClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ShortcutEntryViewModel vm)
        {
            vm.Gesture = vm.DefaultGesture;
        }
    }
}

