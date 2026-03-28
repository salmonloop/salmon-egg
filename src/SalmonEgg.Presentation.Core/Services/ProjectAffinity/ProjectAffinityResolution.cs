namespace SalmonEgg.Presentation.Core.Services.ProjectAffinity;

public enum ProjectAffinitySource
{
    Override,
    PathMapping,
    DirectMatch,
    NeedsMapping,
    Unclassified
}

public sealed record ProjectAffinityResolution(
    string EffectiveProjectId,
    ProjectAffinitySource Source,
    string? MatchedProjectId,
    string? OverrideProjectId,
    string? RemoteCwd,
    string? LocalResolvedPath,
    bool NeedsUserAttention,
    string Reason);
