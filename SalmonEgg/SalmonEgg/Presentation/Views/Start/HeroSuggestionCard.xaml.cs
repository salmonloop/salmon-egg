using System;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Start;

namespace SalmonEgg.Presentation.Views.Start;

public sealed partial class HeroSuggestionCard : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty SuggestionProperty =
        DependencyProperty.Register(
            nameof(Suggestion),
            typeof(QuickSuggestionViewModel),
            typeof(HeroSuggestionCard),
            new PropertyMetadata(null, OnSuggestionChanged));

    public event PropertyChangedEventHandler? PropertyChanged;

    private QuickSuggestionViewModel? _observedSuggestion;

    public QuickSuggestionViewModel? Suggestion
    {
        get => (QuickSuggestionViewModel?)GetValue(SuggestionProperty);
        set => SetValue(SuggestionProperty, value);
    }

    public string AutomationId => Suggestion?.AutomationId ?? string.Empty;

    public string Icon => Suggestion?.Icon ?? string.Empty;

    public string Title => Suggestion?.Title ?? string.Empty;

    public string Subtitle => Suggestion?.Subtitle ?? string.Empty;

    public ICommand? ActionCommand => Suggestion?.ActionCommand;

    public HeroSuggestionCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void OnSuggestionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((HeroSuggestionCard)d).OnSuggestionChanged(
            e.OldValue as QuickSuggestionViewModel,
            e.NewValue as QuickSuggestionViewModel);
    }

    private void OnSuggestionChanged(QuickSuggestionViewModel? oldSuggestion, QuickSuggestionViewModel? newSuggestion)
    {
        if (ReferenceEquals(_observedSuggestion, oldSuggestion))
        {
            UnhookSuggestion();
        }

        HookSuggestion(newSuggestion);

        RaiseAllProjectionPropertiesChanged();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HookSuggestion(Suggestion);
        RaiseAllProjectionPropertiesChanged();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnhookSuggestion();
    }

    private void HookSuggestion(QuickSuggestionViewModel? suggestion)
    {
        if (suggestion is null || ReferenceEquals(_observedSuggestion, suggestion))
        {
            return;
        }

        UnhookSuggestion();
        suggestion.PropertyChanged += OnSuggestionPropertyChanged;
        _observedSuggestion = suggestion;
    }

    private void UnhookSuggestion()
    {
        if (_observedSuggestion is null)
        {
            return;
        }

        _observedSuggestion.PropertyChanged -= OnSuggestionPropertyChanged;
        _observedSuggestion = null;
    }

    private void OnSuggestionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            RaiseAllProjectionPropertiesChanged();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(QuickSuggestionViewModel.AutomationId), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(AutomationId));
        }
        else if (string.Equals(e.PropertyName, nameof(QuickSuggestionViewModel.Icon), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(Icon));
        }
        else if (string.Equals(e.PropertyName, nameof(QuickSuggestionViewModel.Title), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(Title));
        }
        else if (string.Equals(e.PropertyName, nameof(QuickSuggestionViewModel.Subtitle), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(Subtitle));
        }
    }

    private void RaiseAllProjectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(Suggestion));
        OnPropertyChanged(nameof(AutomationId));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ActionCommand));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
