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
    private int _activeSuggestionIndex = -1;

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

    private void OnHeroSuggestionButtonLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.GotFocus -= OnHeroSuggestionButtonGotFocus;
            button.GotFocus += OnHeroSuggestionButtonGotFocus;
        }

        RefreshHeroSuggestionFocusTargets();
    }

    private void OnHeroSuggestionButtonGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var automationId = AutomationProperties.GetAutomationId(button);
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return;
        }

        for (var i = 0; i < ViewModel.Suggestions.Count; i++)
        {
            if (string.Equals(ViewModel.Suggestions[i].AutomationId, automationId, StringComparison.Ordinal))
            {
                _activeSuggestionIndex = i;
                return;
            }
        }
    }

    public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        if (!IsFocusWithinHeroSuggestions())
        {
            return false;
        }

        return intent switch
        {
            GamepadNavigationIntent.MoveLeft => TryMoveFocusedSuggestion(-1),
            GamepadNavigationIntent.MoveRight => TryMoveFocusedSuggestion(1),
            GamepadNavigationIntent.MoveDown => TryFocusPromptBox(),
            _ => false
        };
    }

    public bool TryFocusPrimaryContentTarget()
    {
        if (ViewModel.Suggestions.Count > 0
            && FindSuggestionButton(ViewModel.Suggestions[0].AutomationId) is Button firstSuggestion)
        {
            _activeSuggestionIndex = 0;
            var focused = firstSuggestion.Focus(FocusState.Programmatic);
            App.BootLog($"StartView.TryFocusPrimaryContentTarget target={ViewModel.Suggestions[0].AutomationId} focused={focused}");
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                var reaffirmed = firstSuggestion.Focus(FocusState.Programmatic);
                App.BootLog($"StartView.TryFocusPrimaryContentTarget.Reaffirm target={ViewModel.Suggestions[0].AutomationId} focused={reaffirmed}");
            });
            return focused;
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

    private bool TryMoveFocusedSuggestion(int delta)
    {
        if (ViewModel.Suggestions.Count == 0)
        {
            return false;
        }

        var currentIndex = _activeSuggestionIndex >= 0
            ? _activeSuggestionIndex
            : ResolveFocusedSuggestionIndex();
        if (currentIndex < 0)
        {
            return false;
        }

        var nextIndex = Math.Clamp(currentIndex + delta, 0, ViewModel.Suggestions.Count - 1);
        if (nextIndex == currentIndex)
        {
            return false;
        }

        var targetId = ViewModel.Suggestions[nextIndex].AutomationId;
        App.BootLog($"StartView.TryMoveFocusedSuggestion current={currentIndex} next={nextIndex} target={targetId}");
        if (FindSuggestionButton(targetId) is not Button nextButton)
        {
            return false;
        }

        var focused = nextButton.Focus(FocusState.Programmatic);
        if (focused)
        {
            _activeSuggestionIndex = nextIndex;
        }

        return focused;
    }

    private void RefreshHeroSuggestionFocusTargets()
    {
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

            App.BootLog(
                $"StartView.RefreshHeroSuggestionFocusTargets target={ViewModel.Suggestions[i].AutomationId} left={DescribeTarget(button.XYFocusLeft)} right={DescribeTarget(button.XYFocusRight)}");
        }
    }

    private int ResolveFocusedSuggestionIndex()
    {
        if (HeroSuggestionsHost.XamlRoot is null)
        {
            return -1;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(HeroSuggestionsHost.XamlRoot) as DependencyObject;
        while (current is not null)
        {
            if (current is Button button)
            {
                var automationId = AutomationProperties.GetAutomationId(button);
                for (var i = 0; i < ViewModel.Suggestions.Count; i++)
                {
                    if (string.Equals(ViewModel.Suggestions[i].AutomationId, automationId, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return -1;
    }

    private static string DescribeTarget(DependencyObject? target)
    {
        return target is Button button
            ? AutomationProperties.GetAutomationId(button) ?? "<button>"
            : target?.GetType().Name ?? "<null>";
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
