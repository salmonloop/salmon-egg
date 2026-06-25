using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public static class StartSessionCwdResolver
{
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

        return ProjectSelectionCwdResolver.ResolveCwd(lastSelectedProjectId, projects, remoteDirectories);
    }
}
