using System;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Views.Chat;

public sealed partial class SessionSettingsDialog : ContentDialog
{
    public SessionSettingsDialog(ChatViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }

    public ChatViewModel ViewModel { get; }
}
