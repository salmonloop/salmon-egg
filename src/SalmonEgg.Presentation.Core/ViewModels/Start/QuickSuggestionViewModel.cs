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

    public QuickSuggestionViewModel(string icon, string title, string subtitle, string prompt, IRelayCommand actionCommand)
    {
        Icon = icon;
        Title = title;
        Subtitle = subtitle;
        Prompt = prompt;
        AutomationId = $"StartView.Suggestion.{CreateSlug(title)}";
        ActionCommand = actionCommand;
    }

    private static string CreateSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("代码库", "Codebase", StringComparison.Ordinal)
            .Replace("分析", "Analyze", StringComparison.Ordinal)
            .Replace("推荐", "Recommend", StringComparison.Ordinal)
            .Replace("开发任务", "Tasks", StringComparison.Ordinal)
            .Replace("解决", "Resolve", StringComparison.Ordinal)
            .Replace("最近报错", "Errors", StringComparison.Ordinal);
    }
}
