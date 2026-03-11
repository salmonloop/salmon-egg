using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Views.Start;

public sealed partial class StartView : Page
{
    private readonly IShellNavigationService _shellNavigation;
    private readonly ChatViewModel _chatViewModel;

    public StartView()
    {
        _shellNavigation = App.ServiceProvider.GetRequiredService<IShellNavigationService>();
        _chatViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();

        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _chatViewModel.RestoreConversationsAsync();
            await _chatViewModel.TryAutoConnectAsync();
            await _chatViewModel.EnsureAcpProfilesLoadedAsync();
        }
        catch
        {
        }
    }

    private void OnPromptSubmitted(object sender, EventArgs e)
    {
        _shellNavigation.NavigateToChat();
    }
}
