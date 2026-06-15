using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;


<<<<<<< TODO: Unmerged change from project 'SalmonEgg(net10.0-browserwasm)', Before:
namespace SalmonEgg.Presentation.Views
{
public sealed partial class GeneralSettingsPage : SettingsPageBase
{
    public GeneralSettingsViewModel ViewModel { get; }

    public GeneralSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<GeneralSettingsViewModel>();
        this.InitializeComponent();
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.GeneralKey);
    }

    protected override Control? GetSectionEntryFocusTarget()
        => GeneralAutoStartToggle;
}
=======
namespace SalmonEgg.Presentation.Views;

public sealed partial class GeneralSettingsPage : SettingsPageBase
{
public GeneralSettingsViewModel ViewModel { get; }

public GeneralSettingsPage()
{
    ViewModel = App.ServiceProvider.GetRequiredService<GeneralSettingsViewModel>();
    this.InitializeComponent();
    SetSettingsBreadcrumbForSection(SettingsSectionCatalog.GeneralKey);
}

protected override Control? GetSectionEntryFocusTarget()
    => GeneralAutoStartToggle;
>>>>>>> After
namespace SalmonEgg.Presentation.Views;

public sealed partial class GeneralSettingsPage : SettingsPageBase
{
    public GeneralSettingsViewModel ViewModel { get; }

    public GeneralSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<GeneralSettingsViewModel>();
        this.InitializeComponent();
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.GeneralKey);
    }

    protected override Control? GetSectionEntryFocusTarget()
        => GeneralAutoStartToggle;
}
