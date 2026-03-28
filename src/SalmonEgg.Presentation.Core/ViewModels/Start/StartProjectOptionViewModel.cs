namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed class StartProjectOptionViewModel
{
    public StartProjectOptionViewModel(string projectId, string displayName)
    {
        ProjectId = projectId ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
    }

    public string ProjectId { get; }

    public string DisplayName { get; }
}
