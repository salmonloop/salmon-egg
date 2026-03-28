using System.Collections.Generic;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;

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
    IReadOnlyList<ProjectPathMapping> PathMappings,
    string UnclassifiedProjectId);
