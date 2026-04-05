using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Discover;

namespace SalmonEgg.Presentation.Views.Discover;

public sealed partial class DiscoverSessionsPage : Page
{
    public DiscoverSessionsViewModel ViewModel { get; }

    public DiscoverSessionsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<DiscoverSessionsViewModel>();
        this.InitializeComponent();
        DataContext = ViewModel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SkeletonPulse?.Begin();
        if (ViewModel.InitializeCommand.CanExecute(null))
        {
            await ViewModel.InitializeCommand.ExecuteAsync(null);
        }
    }

    private void OnLoadSessionClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var session = button.CommandParameter as DiscoverSessionItemViewModel
            ?? button.DataContext as DiscoverSessionItemViewModel;
        if (session == null || !ViewModel.LoadSessionCommand.CanExecute(session))
        {
            return;
        }

        ViewModel.LoadSessionCommand.Execute(session);
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SkeletonPulse?.Stop();
    }
}
