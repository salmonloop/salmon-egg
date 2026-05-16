using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Domain.Models;
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
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        var isNarrow = e.NewSize.Width < 768;
        ViewModel.SetLayoutMode(isNarrow ? DiscoverLayoutMode.Narrow : DiscoverLayoutMode.Wide);
    }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.SetLayoutMode(ActualWidth < 768 ? DiscoverLayoutMode.Narrow : DiscoverLayoutMode.Wide);
        if (ViewModel.InitializeCommand.CanExecute(null))
        {
            await ViewModel.InitializeCommand.ExecuteAsync(null);
        }
    }

    private void OnProfileItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ServerConfiguration profile)
        {
            return;
        }

        var wasAlreadySelected = ReferenceEquals(ViewModel.SelectedProfile, profile);
        if (wasAlreadySelected && ViewModel.OpenProfileDetailsCommand.CanExecute(null))
        {
            ViewModel.OpenProfileDetailsCommand.Execute(null);
        }
    }
}
