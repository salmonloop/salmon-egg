namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed class ProjectOptionViewModel
{
    public ProjectOptionViewModel(string? projectId, string name, string? rootPath)
    {
        ProjectId = projectId;
        Name = name;
        RootPath = rootPath;
    }

    public string? ProjectId { get; }
    public string Name { get; }
    public string? RootPath { get; }
}
