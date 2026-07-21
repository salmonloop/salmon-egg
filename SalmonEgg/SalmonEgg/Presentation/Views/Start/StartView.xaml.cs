using System;
using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.ViewModels.Start;

namespace SalmonEgg.Presentation.Views.Start;

public sealed partial class StartView : Page, IPrimaryContentFocusTarget
{
    public StartViewModel ViewModel { get; }

    public bool IsGuiAutomationMode { get; }

    private bool _isSuggestionCollectionHooked;

    public StartView()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<StartViewModel>();
        IsGuiAutomationMode = string.Equals(
            Environment.GetEnvironmentVariable("SALMONEGG_GUI"),
            "1",
            StringComparison.Ordinal);

        InitializeComponent();
        ComposerShell.MoveUpEscapeHandler = HandlePromptMoveUpEscape;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        HookSuggestionCollection();
        HeroSuggestionLayoutStates.CurrentStateChanged += OnHeroSuggestionLayoutStateChanged;
        ViewModel.OnComposerLoaded();
        RefreshHeroSuggestionFocusTargets();
        _ = DispatcherQueue.TryEnqueue(RefreshHeroSuggestionFocusTargets);

        // ViewModel owns ACP profile / conversation-restore error logging; shell must
        // not silence activation failures here.
        var ensureAcpProfilesLoadedTask = ViewModel.Chat.EnsureAcpProfilesLoadedAsync();
        await ViewModel.Chat.RestoreConversationsAsync();
        await ensureAcpProfilesLoadedTask;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnhookSuggestionCollection();
        HeroSuggestionLayoutStates.CurrentStateChanged -= OnHeroSuggestionLayoutStateChanged;
        ViewModel.OnComposerUnloaded();
    }

    private void OnHeroSuggestionCardLoaded(object sender, RoutedEventArgs e)
    {
        RefreshHeroSuggestionFocusTargets();
    }

    private void OnHeroSuggestionLayoutStateChanged(object? sender, VisualStateChangedEventArgs e)
    {
        // The adaptive ItemsPanel swap rearranges the suggestion buttons; Loaded does not re-fire
        // when only the panel changes, so re-wire directional focus targets for the new layout.
        _ = DispatcherQueue.TryEnqueue(RefreshHeroSuggestionFocusTargets);
    }

    private void HookSuggestionCollection()
    {
        if (_isSuggestionCollectionHooked)
        {
            return;
        }

        ViewModel.Suggestions.CollectionChanged += OnSuggestionsChanged;
        _isSuggestionCollectionHooked = true;
    }

    private void UnhookSuggestionCollection()
    {
        if (!_isSuggestionCollectionHooked)
        {
            return;
        }

        ViewModel.Suggestions.CollectionChanged -= OnSuggestionsChanged;
        _isSuggestionCollectionHooked = false;
    }

    private void OnSuggestionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshHeroSuggestionFocusTargets();
        _ = DispatcherQueue.TryEnqueue(RefreshHeroSuggestionFocusTargets);
    }

    public bool TryFocusPrimaryContentTarget()
    {
        if (ViewModel.Suggestions.Count > 0
            && FindSuggestionButton(ViewModel.Suggestions[0].AutomationId) is Button firstSuggestion)
        {
            return firstSuggestion.Focus(FocusState.Keyboard);
        }

        return TryFocusPromptBox();
    }

    public bool HandlePromptMoveUpEscape()
    {
        return TryFocusPrimaryContentTarget();
    }

    private bool TryFocusPromptBox()
    {
        return FindPromptBox() is TextBox promptBox
            && promptBox.Focus(FocusState.Keyboard);
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

        // Default ItemsPanel is vertical (Narrow); Wide is only active when the AdaptiveTrigger matches.
        var isWideLayout = string.Equals(
            HeroSuggestionLayoutStates.CurrentState?.Name,
            "Wide",
            StringComparison.Ordinal);

        for (var i = 0; i < ViewModel.Suggestions.Count; i++)
        {
            if (FindSuggestionButton(ViewModel.Suggestions[i].AutomationId) is not Button button)
            {
                continue;
            }

            var previousButton = i > 0
                ? FindSuggestionButton(ViewModel.Suggestions[i - 1].AutomationId)
                : null;
            var nextButton = i + 1 < ViewModel.Suggestions.Count
                ? FindSuggestionButton(ViewModel.Suggestions[i + 1].AutomationId)
                : null;
            var promptFocusTarget = FindPromptBox();

            if (isWideLayout)
            {
                // Horizontal row: Left/Right neighbors; Down to the composer prompt.
                if (previousButton is not null)
                {
                    button.XYFocusLeft = previousButton;
                }
                else
                {
                    button.ClearValue(Control.XYFocusLeftProperty);
                }

                if (nextButton is not null)
                {
                    button.XYFocusRight = nextButton;
                }
                else
                {
                    button.ClearValue(Control.XYFocusRightProperty);
                }

                button.ClearValue(Control.XYFocusUpProperty);

                if (promptFocusTarget is not null)
                {
                    button.XYFocusDown = promptFocusTarget;
                }
                else
                {
                    button.ClearValue(Control.XYFocusDownProperty);
                }
            }
            else
            {
                // Vertical stack: Up/Down neighbors; only the last card drops into the prompt.
                button.ClearValue(Control.XYFocusLeftProperty);
                button.ClearValue(Control.XYFocusRightProperty);

                if (previousButton is not null)
                {
                    button.XYFocusUp = previousButton;
                }
                else
                {
                    button.ClearValue(Control.XYFocusUpProperty);
                }

                if (nextButton is not null)
                {
                    button.XYFocusDown = nextButton;
                }
                else if (promptFocusTarget is not null)
                {
                    button.XYFocusDown = promptFocusTarget;
                }
                else
                {
                    button.ClearValue(Control.XYFocusDownProperty);
                }
            }
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

        return default;
    }
}
