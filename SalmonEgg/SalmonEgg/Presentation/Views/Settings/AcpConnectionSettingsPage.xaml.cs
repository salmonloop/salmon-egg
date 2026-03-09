using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class AcpConnectionSettingsPage : Page
{
    public AcpConnectionSettingsViewModel ViewModel { get; }

    public AcpConnectionSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<AcpConnectionSettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.Profiles.RefreshCommand.ExecuteAsync(null);
    }

    private async void OnAddProfileClick(object sender, RoutedEventArgs e)
    {
        var editorVm = App.ServiceProvider.GetRequiredService<ConfigurationEditorViewModel>();
        editorVm.LoadNewFromTransportConfig(ViewModel.Chat.TransportConfig, "新预设");

        var dialog = new ConfigurationEditorDialog(editorVm);
        dialog.XamlRoot = XamlRoot;
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.Profiles.RefreshCommand.ExecuteAsync(null);
            ViewModel.Profiles.SelectedProfile = ViewModel.Profiles.Profiles.FirstOrDefault(p => p.Id == editorVm.Configuration.Id);
        }
    }

    private async void OnEditProfileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ServerConfiguration config)
        {
            return;
        }

        var editorVm = App.ServiceProvider.GetRequiredService<ConfigurationEditorViewModel>();
        editorVm.LoadConfiguration(config);

        var dialog = new ConfigurationEditorDialog(editorVm);
        dialog.XamlRoot = XamlRoot;
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.Profiles.RefreshCommand.ExecuteAsync(null);
            ViewModel.Profiles.SelectedProfile = ViewModel.Profiles.Profiles.FirstOrDefault(p => p.Id == editorVm.Configuration.Id);
        }
    }

    private async void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ServerConfiguration config)
        {
            return;
        }

        await ViewModel.Profiles.DeleteCommand.ExecuteAsync(config);
    }

    private async void OnEditProfileMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not ServerConfiguration config)
        {
            return;
        }

        var editorVm = App.ServiceProvider.GetRequiredService<ConfigurationEditorViewModel>();
        editorVm.LoadConfiguration(config);

        var dialog = new ConfigurationEditorDialog(editorVm);
        dialog.XamlRoot = XamlRoot;
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.Profiles.RefreshCommand.ExecuteAsync(null);
            ViewModel.Profiles.SelectedProfile = ViewModel.Profiles.Profiles.FirstOrDefault(p => p.Id == editorVm.Configuration.Id);
        }
    }

    private async void OnDeleteProfileMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not ServerConfiguration config)
        {
            return;
        }

        await ViewModel.Profiles.DeleteCommand.ExecuteAsync(config);
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
