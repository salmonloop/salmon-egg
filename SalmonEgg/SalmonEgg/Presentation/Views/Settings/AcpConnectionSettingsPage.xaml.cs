using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class AcpConnectionSettingsPage : SettingsPageBase
{
    public AcpConnectionSettingsViewModel ViewModel { get; }

    public AcpConnectionSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<AcpConnectionSettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        SetSettingsBreadcrumbFromResource("SettingsNav_AgentAcp.Content", "ACP / Agent");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.Profiles.RefreshCommand.ExecuteAsync(null);
    }

    private void OnAddProfileClick(object sender, RoutedEventArgs e)
    {
        Frame?.Navigate(typeof(AgentProfileEditorPage), new AgentProfileEditorArgs(isEditing: false, profileId: null));
    }

    private void OnEditProfileMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string profileId)
        {
            return;
        }

        Frame?.Navigate(typeof(AgentProfileEditorPage), new AgentProfileEditorArgs(isEditing: true, profileId: profileId));
    }


    private async void OnDeleteProfileMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string profileId)
        {
            return;
        }

        var config = ViewModel.Profiles.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (config != null)
        {
            await ViewModel.Profiles.DeleteCommand.ExecuteAsync(config);
        }
    }

    private async void OnProfileConnectionToggleToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || toggle.DataContext is not AgentProfileItemViewModel item)
        {
            return;
        }

        // Ignore programmatic state synchronization; only react to user-initiated toggles.
        if (toggle.IsOn == item.IsConnected)
        {
            return;
        }

        if (!item.ToggleConnectionCommand.CanExecute(null))
        {
            return;
        }

        await item.ToggleConnectionCommand.ExecuteAsync(null);
    }
}
