using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.ViewModels.Start;

namespace SalmonEgg.Presentation.Views.Start;

public sealed partial class StartView : Page, INavigationIntentConsumer
{
    public StartViewModel ViewModel { get; }

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

    private void OnHeroSuggestionItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is QuickSuggestionViewModel suggestion)
        {
            ViewModel.ExecuteSuggestionCommand.Execute(suggestion);
        }
    }

    private void OnHeroSuggestionsContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is ListViewItem container
            && args.Item is QuickSuggestionViewModel suggestion)
        {
            AutomationProperties.SetAutomationId(container, suggestion.AutomationId);
            AutomationProperties.SetName(container, suggestion.Title);

            var itemCount = sender.Items.Count;
            var index = args.ItemIndex;
            container.XYFocusLeft = index > 0
                ? sender.ContainerFromIndex(index - 1) as DependencyObject
                : null;
            container.XYFocusRight = index + 1 < itemCount
                ? sender.ContainerFromIndex(index + 1) as DependencyObject
                : null;
            container.XYFocusDown = FindPromptBox();
        }
    }

    public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        if (!IsFocusWithinHeroSuggestions())
        {
            return false;
        }

        var consumed = intent switch
        {
            GamepadNavigationIntent.MoveLeft => TryMoveHeroSuggestionSelection(-1),
            GamepadNavigationIntent.MoveRight => TryMoveHeroSuggestionSelection(1),
            GamepadNavigationIntent.MoveDown => TryFocusPromptBox(),
            GamepadNavigationIntent.Activate => TryActivateSelectedHeroSuggestion(),
            _ => false
        };

        App.BootLog($"StartView.TryConsumeNavigationIntent intent={intent} consumed={consumed}");
        return consumed;
    }

    private bool IsFocusWithinHeroSuggestions()
    {
        if (HeroSuggestionsList.XamlRoot is null)
        {
            return false;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(HeroSuggestionsList.XamlRoot) as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, HeroSuggestionsList))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool TryMoveHeroSuggestionSelection(int delta)
    {
        if (HeroSuggestionsList.Items.Count == 0)
        {
            return false;
        }

        var currentIndex = HeroSuggestionsList.SelectedIndex;
        if (currentIndex < 0)
        {
            currentIndex = ResolveFocusedHeroSuggestionIndex();
        }

        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = Math.Clamp(currentIndex + delta, 0, HeroSuggestionsList.Items.Count - 1);
        if (nextIndex == currentIndex && HeroSuggestionsList.SelectedIndex == nextIndex)
        {
            App.BootLog($"StartView.TryMoveHeroSuggestionSelection noop current={currentIndex} selected={HeroSuggestionsList.SelectedIndex}");
            return false;
        }

        HeroSuggestionsList.SelectedIndex = nextIndex;
        HeroSuggestionsList.ScrollIntoView(HeroSuggestionsList.Items[nextIndex]);
        if (HeroSuggestionsList.ContainerFromIndex(nextIndex) is ListViewItem container)
        {
            var focused = container.Focus(FocusState.Programmatic);
            App.BootLog($"StartView.TryMoveHeroSuggestionSelection current={currentIndex} next={nextIndex} containerFocus={focused}");
            return focused;
        }

        var fallbackFocused = HeroSuggestionsList.Focus(FocusState.Programmatic);
        App.BootLog($"StartView.TryMoveHeroSuggestionSelection current={currentIndex} next={nextIndex} listFallback={fallbackFocused}");
        return fallbackFocused;
    }

    private int ResolveFocusedHeroSuggestionIndex()
    {
        if (HeroSuggestionsList.XamlRoot is null)
        {
            return -1;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(HeroSuggestionsList.XamlRoot) as DependencyObject;
        while (current is not null)
        {
            if (current is ListViewItem item)
            {
                return HeroSuggestionsList.IndexFromContainer(item);
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return -1;
    }

    private bool TryFocusPromptBox()
    {
        return FindPromptBox() is TextBox promptBox
            && promptBox.Focus(FocusState.Programmatic);
    }

    private bool TryActivateSelectedHeroSuggestion()
    {
        var selectedSuggestion = HeroSuggestionsList.SelectedItem as QuickSuggestionViewModel;
        if (selectedSuggestion is null)
        {
            var focusedIndex = ResolveFocusedHeroSuggestionIndex();
            if (focusedIndex >= 0 && focusedIndex < HeroSuggestionsList.Items.Count)
            {
                selectedSuggestion = HeroSuggestionsList.Items[focusedIndex] as QuickSuggestionViewModel;
            }
        }

        if (selectedSuggestion is null)
        {
            return false;
        }

        ViewModel.ExecuteSuggestionCommand.Execute(selectedSuggestion);
        return true;
    }

    private DependencyObject? FindPromptBox()
    {
        return FindDescendant<TextBox>(ComposerShell, static textBox =>
            string.Equals(AutomationProperties.GetAutomationId(textBox), "StartView.PromptBox", StringComparison.Ordinal)
            || string.Equals(textBox.Name, "InputBox", StringComparison.Ordinal));
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
