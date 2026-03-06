using System;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Presentation.ViewModels;

namespace UnoAcpClient.Presentation.Views;

public sealed partial class ConfigurationEditorDialog : ContentDialog
{
    private readonly ConfigurationEditorViewModel _viewModel;

    public ConfigurationEditorDialog(ConfigurationEditorViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.InitializeComponent();
        PopulateFields();
    }

    private void PopulateFields()
    {
        NameTextBox.Text = _viewModel.Name;
        ServerUrlTextBox.Text = _viewModel.ServerUrl;
        TransportComboBox.SelectedIndex = _viewModel.Transport == TransportType.WebSocket ? 0 : 1;
        TokenBox.Password = _viewModel.Token ?? string.Empty;
        ApiKeyBox.Password = _viewModel.ApiKey ?? string.Empty;
        HeartbeatBox.Value = _viewModel.HeartbeatInterval;
        TimeoutBox.Value = _viewModel.ConnectionTimeout;
        ProxyEnabledSwitch.IsOn = _viewModel.ProxyEnabled;
        ProxyUrlBox.Text = _viewModel.ProxyUrl ?? string.Empty;
        UpdateProxyPanelVisibility();
        ProxyEnabledSwitch.Toggled += (s, e) => UpdateProxyPanelVisibility();
    }

    private void UpdateProxyPanelVisibility()
    {
        ProxyPanel.Visibility = ProxyEnabledSwitch.IsOn 
            ? Microsoft.UI.Xaml.Visibility.Visible 
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private async void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        _viewModel.Name = NameTextBox.Text;
        _viewModel.ServerUrl = ServerUrlTextBox.Text;
        _viewModel.Transport = TransportComboBox.SelectedIndex == 0 ? TransportType.WebSocket : TransportType.HttpSse;
        _viewModel.Token = TokenBox.Password;
        _viewModel.ApiKey = ApiKeyBox.Password;
        _viewModel.HeartbeatInterval = (int)HeartbeatBox.Value;
        _viewModel.ConnectionTimeout = (int)TimeoutBox.Value;
        _viewModel.ProxyEnabled = ProxyEnabledSwitch.IsOn;
        _viewModel.ProxyUrl = ProxyUrlBox.Text;

        await _viewModel.SaveConfigurationAsync();
        
        if (string.IsNullOrEmpty(_viewModel.ErrorMessage))
        {
            args.Cancel = false;
        }
        else
        {
            ErrorTextBlock.Text = _viewModel.ErrorMessage;
            ErrorTextBlock.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }

    private void OnCancelClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _viewModel.Cancel();
    }

    public async System.Threading.Tasks.Task HideAsync()
    {
        this.Hide();
    }
}
