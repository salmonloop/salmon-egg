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
    string UnclassifiedProjectId);
