using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class AboutPage : SettingsPageBase
{
    public AboutViewModel ViewModel { get; }

    public AboutPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<AboutViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.AboutKey);
    }

    protected override Control? GetSectionEntryFocusTarget()
        => FirstAvailableSectionEntryTarget(
            AboutOpenAppDataButton,
            AboutOpenReleaseNotesButton,
            AboutOpenPrivacyPolicyButton,
            AboutCopyVersionInfoButton);
}
