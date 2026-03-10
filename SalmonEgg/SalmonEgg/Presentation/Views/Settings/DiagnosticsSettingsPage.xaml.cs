using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class DiagnosticsSettingsPage : SettingsPageBase
{
    public DiagnosticsSettingsViewModel ViewModel { get; }

    public DiagnosticsSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<DiagnosticsSettingsViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumb("诊断与日志");
    }
}
