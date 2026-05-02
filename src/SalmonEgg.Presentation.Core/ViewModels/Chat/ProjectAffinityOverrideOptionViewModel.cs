namespace SalmonEgg.Presentation.ViewModels.Chat;

public sealed class ProjectAffinityOverrideOptionViewModel
{
    public ProjectAffinityOverrideOptionViewModel(string projectId, string displayName)
    {
        ProjectId = projectId ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
    }

    public string ProjectId { get; }

    public string DisplayName { get; }
}
