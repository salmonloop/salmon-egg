using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Start;

namespace SalmonEgg.Presentation.Views.Start;

public sealed partial class StartView : Page
{
    public StartViewModel ViewModel { get; }

    public StartView()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<StartViewModel>();

        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.Chat.RestoreConversationsAsync();
            await ViewModel.Chat.EnsureAcpProfilesLoadedAsync();
        }
        catch
        {
        }
    }
}
