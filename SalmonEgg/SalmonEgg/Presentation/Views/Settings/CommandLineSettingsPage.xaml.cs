using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class CommandLineSettingsPage : SalmonEgg.Presentation.Views.SettingsPageBase
{
    public CommandLineSettingsViewModel ViewModel { get; }

    public CommandLineSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<CommandLineSettingsViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.CommandLineKey);
    }

    /// <summary>
    /// Re-inspects on every arrival rather than once per app run.
    /// </summary>
    /// <remarks>
    /// PATH is a machine fact an installer or another install can change while the app is open, so a value
    /// read at startup would go stale silently. The command owns its own failure handling, so the returned
    /// task is deliberately not awaited here: navigation must not block on starting a child process.
    /// </remarks>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    protected override Control? GetSectionEntryFocusTarget()
        => CommandLineRefreshButton;
}
