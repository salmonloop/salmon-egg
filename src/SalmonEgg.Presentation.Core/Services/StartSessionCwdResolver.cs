using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public static class StartSessionCwdResolver
{
    private const string RemoteDirectoryProjectIdPrefix = "remote-directory:";

    public static string? Resolve(
        string? pendingProjectRootPath,
        string? lastSelectedProjectId,
        IReadOnlyList<ProjectDefinition> projects,
        IReadOnlyList<AgentRemoteDirectory> remoteDirectories)
    {
        var pending = SessionCwdResolver.Resolve(pendingProjectRootPath, null);
        if (!string.IsNullOrWhiteSpace(pending))
        {
            return pending;
        }

        var selectedProjectId = Normalize(lastSelectedProjectId);
        if (string.IsNullOrWhiteSpace(selectedProjectId))
        {
            return null;
        }

        var remoteDirectoryId = TryParseRemoteDirectoryId(selectedProjectId);
        if (!string.IsNullOrWhiteSpace(remoteDirectoryId))
        {
            return Normalize(remoteDirectories.FirstOrDefault(directory =>
                string.Equals(directory.DirectoryId, remoteDirectoryId, StringComparison.Ordinal))?.RemotePath);
        }

        return Normalize(projects.FirstOrDefault(project =>
            string.Equals(project.ProjectId, selectedProjectId, StringComparison.Ordinal))?.RootPath);
    }

    private static string? TryParseRemoteDirectoryId(string projectId)
    {
        if (!projectId.StartsWith(RemoteDirectoryProjectIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return Normalize(projectId[RemoteDirectoryProjectIdPrefix.Length..]);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
