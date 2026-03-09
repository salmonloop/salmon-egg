using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class DiagnosticsSettingsPage : Page
{
    public DiagnosticsSettingsViewModel ViewModel { get; }

    public DiagnosticsSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<DiagnosticsSettingsViewModel>();
        InitializeComponent();
    }
}

