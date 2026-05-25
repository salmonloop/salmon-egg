using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.ViewModels.Start;

namespace SalmonEgg.Presentation.Views.Start;

public sealed partial class StartView : Page, INavigationIntentConsumer, IPrimaryContentFocusTarget
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
        RefreshHeroSuggestionFocusTargets();
        _ = DispatcherQueue.TryEnqueue(RefreshHeroSuggestionFocusTargets);

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

    private void OnHeroSuggestionButtonLoaded(object sender, RoutedEventArgs e)
    {
        RefreshHeroSuggestionFocusTargets();
    }

    public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        if (intent == GamepadNavigationIntent.MoveDown && IsFocusWithinHeroSuggestions())
        {
            return TryFocusPromptBox();
        }

        if (intent == GamepadNavigationIntent.MoveUp && IsFocusWithinPromptBox())
        {
            return TryFocusPrimaryContentTarget();
        }

        return false;
    }

    private bool IsFocusWithinPromptBox()
    {
        if (HeroSuggestionsHost.XamlRoot is null)
        {
            return false;
        }

        var promptBox = FindPromptBox();
        if (promptBox is null)
        {
            return false;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(HeroSuggestionsHost.XamlRoot) as DependencyObject;
        return ReferenceEquals(FindAncestorOrSelf<TextBox>(current), promptBox);
    }

    public bool TryFocusPrimaryContentTarget()
    {
        if (ViewModel.Suggestions.Count > 0
            && FindSuggestionButton(ViewModel.Suggestions[0].AutomationId) is Button firstSuggestion)
        {
            return firstSuggestion.Focus(FocusState.Programmatic);
        }

        return TryFocusPromptBox();
    }

    private bool IsFocusWithinHeroSuggestions()
    {
        if (HeroSuggestionsHost.XamlRoot is null)
        {
            return false;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(HeroSuggestionsHost.XamlRoot) as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, HeroSuggestionsHost))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool TryFocusPromptBox()
    {
        return FindPromptBox() is TextBox promptBox
            && promptBox.Focus(FocusState.Programmatic);
    }

    private DependencyObject? FindPromptBox()
    {
        return FindDescendant<TextBox>(ComposerShell, static textBox =>
            string.Equals(AutomationProperties.GetAutomationId(textBox), "StartView.PromptBox", StringComparison.Ordinal)
            || string.Equals(textBox.Name, "InputBox", StringComparison.Ordinal));
    }

    private Button? FindSuggestionButton(string automationId)
    {
        return FindDescendant<Button>(HeroSuggestionsHost, button =>
            string.Equals(AutomationProperties.GetAutomationId(button), automationId, StringComparison.Ordinal));
    }

    private void RefreshHeroSuggestionFocusTargets()
    {
        var firstSuggestion = ViewModel.Suggestions.Count > 0
            ? FindSuggestionButton(ViewModel.Suggestions[0].AutomationId)
            : null;
        if (firstSuggestion is not null
            && FindPromptBox() is TextBox promptBox)
        {
            promptBox.XYFocusUp = firstSuggestion;
        }

        for (var i = 0; i < ViewModel.Suggestions.Count; i++)
        {
            if (FindSuggestionButton(ViewModel.Suggestions[i].AutomationId) is not Button button)
            {
                continue;
            }

            button.XYFocusLeft = i > 0
                ? FindSuggestionButton(ViewModel.Suggestions[i - 1].AutomationId)
                : null;
            button.XYFocusRight = i + 1 < ViewModel.Suggestions.Count
                ? FindSuggestionButton(ViewModel.Suggestions[i + 1].AutomationId)
                : null;
            button.XYFocusDown = FindPromptBox();
        }
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

    private static T? FindAncestorOrSelf<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
