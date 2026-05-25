using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels.Discover;

namespace SalmonEgg.Presentation.Views.Discover;

public sealed partial class DiscoverSessionsPage : Page, INavigationIntentConsumer
{
    public DiscoverSessionsViewModel ViewModel { get; }

    public DiscoverSessionsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<DiscoverSessionsViewModel>();
        this.InitializeComponent();
        DataContext = ViewModel;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
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

    public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        return intent switch
        {
            GamepadNavigationIntent.MoveRight => TryMoveFocusBetweenSessionAndImport(true),
            GamepadNavigationIntent.MoveLeft => TryMoveFocusBetweenSessionAndImport(false),
            _ => false
        };
    }

    private bool TryMoveFocusBetweenSessionAndImport(bool moveRight)
    {
        if (SessionsList.XamlRoot is null)
        {
            return false;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(SessionsList.XamlRoot) as DependencyObject;
        if (current is null)
        {
            return false;
        }

        var importButton = FindAncestorOrSelf<Button>(current, button =>
            string.Equals(AutomationProperties.GetAutomationId(button), "DiscoverSessions.ImportButton", StringComparison.Ordinal));
        if (importButton is not null)
        {
            if (!moveRight && FindAncestorOrSelf<ListViewItem>(current) is { } focusedItemContainer)
            {
                return focusedItemContainer.Focus(FocusState.Programmatic);
            }

            return false;
        }

        var sessionItemContainer = FindAncestorOrSelf<ListViewItem>(current);
        if (sessionItemContainer is null)
        {
            return false;
        }

        if (moveRight && FindDescendant<Button>(sessionItemContainer, button =>
                string.Equals(AutomationProperties.GetAutomationId(button), "DiscoverSessions.ImportButton", StringComparison.Ordinal)) is { } actionButton)
        {
            return actionButton.Focus(FocusState.Programmatic);
        }

        return false;
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject? start, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match && (predicate is null || predicate(match)))
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match))
            {
                return match;
            }

            var nested = FindDescendant(child, predicate);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
