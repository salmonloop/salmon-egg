namespace SalmonEgg.Domain.Models;

public sealed class ProjectDefinition
{
    public string ProjectId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;
}

