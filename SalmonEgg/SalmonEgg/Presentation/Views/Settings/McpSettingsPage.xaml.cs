using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    protected override Control? GetSectionEntryFocusTarget()
        => FirstAvailableSectionEntryTarget(McpReloadButton, McpAddServerButton);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
