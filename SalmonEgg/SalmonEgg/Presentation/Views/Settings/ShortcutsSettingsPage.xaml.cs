using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Controls;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class ShortcutsSettingsPage : SettingsPageBase
{
    public ShortcutsSettingsViewModel ViewModel { get; }

    public ShortcutsSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<ShortcutsSettingsViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.ShortcutsKey);
    }

    protected override Control? GetSectionEntryFocusTarget()
        => FindDescendantControl<ShortcutRecorder>() as Control ?? ShortcutsListView;
}
