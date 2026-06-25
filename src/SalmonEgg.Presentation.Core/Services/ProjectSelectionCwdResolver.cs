using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public static class ProjectSelectionCwdResolver
{
    public const string RemoteDirectoryProjectIdPrefix = "remote-directory:";

    public static string BuildRemoteDirectoryProjectId(string directoryId)
        => RemoteDirectoryProjectIdPrefix + (Normalize(directoryId) ?? string.Empty);

    public static string? TryParseRemoteDirectoryId(string? projectId)
    {
        var normalizedProjectId = Normalize(projectId);
        if (normalizedProjectId is null
            || !normalizedProjectId.StartsWith(RemoteDirectoryProjectIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return Normalize(normalizedProjectId[RemoteDirectoryProjectIdPrefix.Length..]);
    }

    public static string? ResolveCwd(
        string? projectId,
        IReadOnlyList<ProjectDefinition> projects,
        IReadOnlyList<AgentRemoteDirectory> remoteDirectories)
    {
        var selectedProjectId = Normalize(projectId);
        if (selectedProjectId is null
            || string.Equals(selectedProjectId, NavigationProjectIds.Unclassified, StringComparison.Ordinal))
        {
            return null;
        }

        var remoteDirectoryId = TryParseRemoteDirectoryId(selectedProjectId);
        if (remoteDirectoryId is not null)
        {
            return Normalize(remoteDirectories.FirstOrDefault(directory =>
                string.Equals(directory.DirectoryId, remoteDirectoryId, StringComparison.Ordinal))?.RemotePath);
        }

        return ResolveLocalProjectRoot(selectedProjectId, projects);
    }

    private static string? ResolveLocalProjectRoot(
        string? projectId,
        IReadOnlyList<ProjectDefinition> projects)
    {
        var selectedProjectId = Normalize(projectId);
        if (selectedProjectId is null)
        {
            return null;
        }

        return Normalize(projects.FirstOrDefault(project =>
            string.Equals(project.ProjectId, selectedProjectId, StringComparison.Ordinal))?.RootPath);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
