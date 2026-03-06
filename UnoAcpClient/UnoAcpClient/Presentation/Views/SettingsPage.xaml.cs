using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using UnoAcpClient.Application.Services;
using UnoAcpClient.Domain.Services;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Presentation.ViewModels;

namespace UnoAcpClient.Presentation.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }
        private readonly IConfigurationService _configService;
        private readonly ConfigurationEditorViewModel _editorViewModel;

        public SettingsPage()
        {
            this.InitializeComponent();
            ViewModel = App.ServiceProvider.GetRequiredService<SettingsViewModel>();
            _configService = App.ServiceProvider.GetRequiredService<IConfigurationService>();
            _editorViewModel = App.ServiceProvider.GetRequiredService<ConfigurationEditorViewModel>();
            DataContext = ViewModel;
            _ = ViewModel.LoadConfigurationsAsync();
        }

        private void AddConfiguration_Click(object sender, RoutedEventArgs e)
        {
            _editorViewModel.LoadNewConfiguration();
            ShowEditorDialog();
        }

        private void EditConfiguration_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedConfiguration != null)
            {
                _editorViewModel.LoadConfiguration(ViewModel.SelectedConfiguration);
                ShowEditorDialog();
            }
        }

        private async void DeleteConfiguration_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.DeleteConfigurationAsync();
        }

        private async void SaveConfiguration_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.LoadConfigurationsAsync();
        }

        private async void ShowEditorDialog()
        {
            var dialog = new ConfigurationEditorDialog(_editorViewModel);
            dialog.XamlRoot = this.XamlRoot;

            _editorViewModel.OnSaveRequested += async (sender, config) =>
            {
                await _configService.SaveConfigurationAsync(config);
                await ViewModel.LoadConfigurationsAsync();
                await dialog.HideAsync();
            };

            _editorViewModel.OnCancelRequested += async (sender, e) =>
            {
                await dialog.HideAsync();
            };

            await dialog.ShowAsync();
        }
    }
}
