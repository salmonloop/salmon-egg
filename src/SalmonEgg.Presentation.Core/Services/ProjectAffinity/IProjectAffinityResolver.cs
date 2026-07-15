using System.Collections.Generic;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.Services.ProjectAffinity;

public interface IProjectAffinityResolver
{
    ProjectAffinityResolution Resolve(ProjectAffinityRequest request);
}

public sealed record ProjectAffinityRequest(
    string? RemoteCwd,
    string? BoundProfileId,
    string? RemoteSessionId,
    string? OverrideProjectId,
    IReadOnlyList<ProjectDefinition> Projects,
    IReadOnlyList<AgentRemoteDirectory> RemoteDirectories,
    string UnclassifiedProjectId,
    // Remote directory ids the user added to the navigation project list. A matched remote
    // directory only becomes its own project node when its id is a member; non-member matches
    // keep their existing Unclassified affinity so previously-configured sessions do not move.
    IReadOnlyCollection<string>? NavigationRemoteDirectoryIds = null);
