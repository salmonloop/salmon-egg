using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

/// <summary>
/// Hosts the ACP setup wizard. Display and command wiring only: every rule lives in
/// <see cref="AcpSetupWizardViewModel"/>, so the steps stay testable without a view.
/// </summary>
public sealed partial class AcpSetupWizardPage : SettingsPageBase
{
    public AcpSetupWizardViewModel ViewModel { get; }

    public AcpSetupWizardPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<AcpSetupWizardViewModel>();
        InitializeComponent();
        SetBreadcrumb(
            SettingsBreadcrumbItem.Link(ResolveSettingsRootTitle(), SettingsSectionCatalog.GeneralKey),
            SettingsBreadcrumbItem.Link(
                ResolveSettingsSectionTitle(SettingsSectionCatalog.AgentAcpKey),
                SettingsSectionCatalog.AgentAcpKey),
            SettingsBreadcrumbItem.Current(
                ResolveResourceString("AcpSetup_PageTitle.Text", "ACP Setup Wizard")));
    }

    protected override Control? GetSectionEntryFocusTarget()
        => AcpSetupAgentsList;

    private void OnInstallAgentRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AcpSetupAgentRowViewModel row })
        {
            row.RequestInstall();
        }
    }
}
