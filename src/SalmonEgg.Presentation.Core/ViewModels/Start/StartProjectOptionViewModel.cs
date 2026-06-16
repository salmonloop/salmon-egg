namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed class StartProjectOptionViewModel
{
    public StartProjectOptionViewModel(
        string projectId,
        string displayName,
        bool isSelectable = true,
        string? remoteCwd = null)
    {
        ProjectId = projectId ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        IsSelectable = isSelectable;
        RemoteCwd = string.IsNullOrWhiteSpace(remoteCwd) ? null : remoteCwd.Trim();
    }

    public string ProjectId { get; }
    public string DisplayName { get; }
    public bool IsSelectable { get; }
    public string? RemoteCwd { get; }
}
