using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Domain.Models;
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
        SetSettingsBreadcrumb("Agent (ACP)");
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
        if (sender is not MenuFlyoutItem item || item.Tag is not ServerConfiguration config)
        {
            return;
        }

        Frame?.Navigate(typeof(AgentProfileEditorPage), new AgentProfileEditorArgs(isEditing: true, profileId: config.Id));
    }

    private async void OnConnectProfileMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not ServerConfiguration config)
        {
            return;
        }

        await ViewModel.ConnectToProfileAsync(config);
    }

    private async void OnDeleteProfileMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not ServerConfiguration config)
        {
            return;
        }

        await ViewModel.Profiles.DeleteCommand.ExecuteAsync(config);
    }

    private async void OnProfilesDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Ignore double tap on the "..." button area
        if (e.OriginalSource is DependencyObject source)
        {
            var current = source;
            while (current != null)
            {
                if (current is Button)
                {
                    return;
                }

                current = VisualTreeHelper.GetParent(current);
            }
        }

        var config = (e.OriginalSource as FrameworkElement)?.DataContext as ServerConfiguration
                     ?? ViewModel.Profiles.SelectedProfile;
        if (config == null)
        {
            return;
        }

        await ViewModel.ConnectToProfileAsync(config);
    }

    private async void OnConnectionToggleToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle)
        {
            return;
        }

        // Prevent re-entrant toggles while connecting.
        if (ViewModel.Chat.IsConnecting || ViewModel.Chat.IsInitializing)
        {
            return;
        }

        try
        {
            if (toggle.IsOn)
            {
                if (!ViewModel.Chat.IsConnected)
                {
                    await ViewModel.Chat.InitializeAndConnectCommand.ExecuteAsync(null);
                }
            }
            else
            {
                if (ViewModel.Chat.IsConnected)
                {
                    await ViewModel.Chat.DisconnectCommand.ExecuteAsync(null);
                }
            }
        }
        catch
        {
        }
    }
}
