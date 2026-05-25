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
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _automationId = string.Empty;

    public IRelayCommand ActionCommand { get; }

    public QuickSuggestionViewModel(string automationId, string icon, string title, string subtitle, string prompt, IRelayCommand actionCommand)
    {
        AutomationId = automationId;
        Icon = icon;
        Title = title;
        Subtitle = subtitle;
        Prompt = prompt;
        ActionCommand = actionCommand;
    }
}
