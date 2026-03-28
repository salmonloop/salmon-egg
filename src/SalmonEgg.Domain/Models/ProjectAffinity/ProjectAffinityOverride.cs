using System;

namespace SalmonEgg.Domain.Models.ProjectAffinity;

public sealed class ProjectAffinityOverride : IEquatable<ProjectAffinityOverride>
{
    public ProjectAffinityOverride(string projectId)
    {
        ProjectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
    }

    public string ProjectId { get; }

    public bool Equals(ProjectAffinityOverride? other)
        => other is not null
           && string.Equals(ProjectId, other.ProjectId, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is ProjectAffinityOverride other && Equals(other);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(ProjectId);
}
