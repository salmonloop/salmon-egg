using System;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels;

namespace SalmonEgg.Presentation.Views;

public sealed partial class ConfigurationEditorDialog : ContentDialog
{
    public ConfigurationEditorViewModel ViewModel { get; }

    public ConfigurationEditorDialog(ConfigurationEditorViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }

    private async void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        await ViewModel.SaveConfigurationAsync();
        
        if (!ViewModel.HasError)
        {
            args.Cancel = false;
        }
    }

    private void OnCancelClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.Cancel();
    }
}
