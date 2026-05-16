using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.ViewModels.Start;

namespace SalmonEgg.Presentation.Views.Start;

public sealed partial class StartView : Page
{
    public StartViewModel ViewModel { get; }
    public UiMotion Motion => UiMotion.Current;

    public StartView()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<StartViewModel>();

        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnComposerLoaded();

        try
        {
            await ViewModel.Chat.RestoreConversationsAsync();
            await ViewModel.Chat.EnsureAcpProfilesLoadedAsync();
        }
        catch
        {
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnComposerUnloaded();
    }
}
