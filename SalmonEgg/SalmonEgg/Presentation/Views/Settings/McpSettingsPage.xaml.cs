using Microsoft.UI.Xaml;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class McpSettingsPage : SettingsPageBase
{
    public McpSettingsViewModel ViewModel { get; }

    public McpSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<McpSettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.McpKey);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
