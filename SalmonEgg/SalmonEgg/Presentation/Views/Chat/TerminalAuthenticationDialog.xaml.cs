using System;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Views.Chat;

public sealed partial class TerminalAuthenticationDialog : ContentDialog
{
    public TerminalAuthenticationDialog(TerminalAuthenticationViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }

    public TerminalAuthenticationViewModel ViewModel { get; }
}
