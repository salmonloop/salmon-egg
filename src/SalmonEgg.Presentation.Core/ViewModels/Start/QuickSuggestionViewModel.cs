using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed partial class QuickSuggestionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _categoryLabel = string.Empty;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _automationId = string.Empty;

    [ObservableProperty]
    private bool _isInformational;

    public IAsyncRelayCommand<QuickSuggestionViewModel> ActionCommand { get; }

    public QuickSuggestionViewModel(
        string automationId,
        string icon,
        string title,
        string subtitle,
        string categoryLabel,
        string prompt,
        IAsyncRelayCommand<QuickSuggestionViewModel> actionCommand,
        bool isInformational = false)
    {
        AutomationId = automationId;
        Icon = icon;
        Title = title;
        Subtitle = subtitle;
        CategoryLabel = categoryLabel;
        Prompt = prompt;
        ActionCommand = actionCommand ?? throw new System.ArgumentNullException(nameof(actionCommand));
        IsInformational = isInformational;
    }
}
