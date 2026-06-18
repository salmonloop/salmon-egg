using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.ViewModels.Start;

namespace SalmonEgg.Presentation.Views.Start;

public sealed partial class StartView : Page, IPrimaryContentFocusTarget
{
    public StartViewModel ViewModel { get; }

    public bool IsGuiAutomationMode { get; }

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

        for (var i = 0; i < ViewModel.Suggestions.Count; i++)
        {
            if (FindSuggestionButton(ViewModel.Suggestions[i].AutomationId) is not Button button)
            {
                continue;
            }

            var leftButton = i > 0
                ? FindSuggestionButton(ViewModel.Suggestions[i - 1].AutomationId)
                : null;
            var rightButton = i + 1 < ViewModel.Suggestions.Count
                ? FindSuggestionButton(ViewModel.Suggestions[i + 1].AutomationId)
                : null;
            var promptFocusTarget = FindPromptBox();

            if (leftButton is not null)
            {
                button.XYFocusLeft = leftButton;
            }
            else
            {
                button.ClearValue(Control.XYFocusLeftProperty);
            }

            if (rightButton is not null)
            {
                button.XYFocusRight = rightButton;
            }
            else
            {
                button.ClearValue(Control.XYFocusRightProperty);
            }

            if (promptFocusTarget is not null)
            {
                button.XYFocusDown = promptFocusTarget;
            }
            else
            {
                button.ClearValue(Control.XYFocusDownProperty);
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
