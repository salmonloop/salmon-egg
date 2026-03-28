namespace SalmonEgg.Domain.Models.ProjectAffinity;

public sealed class ProjectPathMapping
{
    public string ProfileId { get; set; } = string.Empty;

    public string RemoteRootPath { get; set; } = string.Empty;

    public string LocalRootPath { get; set; } = string.Empty;
}
