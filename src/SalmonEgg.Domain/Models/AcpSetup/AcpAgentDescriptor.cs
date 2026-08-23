using System;
using System.Collections.Generic;
using System.Linq;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// One agent the wizard knows how to configure: the runtime the user installs, the ACP adapters
/// that can front it, and the launch template each adapter needs.
/// </summary>
public sealed class AcpAgentDescriptor
{
    /// <summary>Stable identifier, aligned with the ACP registry agent id where one exists.</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The agent runtime itself (for example the Claude Code CLI). Detected first so the wizard can
    /// tell "agent missing" apart from "adapter missing".
    /// </summary>
    public required AcpComponentDescriptor Runtime { get; init; }

    /// <summary>Adapters that can expose this agent over ACP, in display order.</summary>
    public required IReadOnlyList<AcpAdapterDescriptor> Adapters { get; init; }

    /// <summary>
    /// The adapter the wizard preselects. Falls back to the first declared adapter when the
    /// recommendation is unset or unknown.
    /// </summary>
    public string RecommendedAdapterId { get; init; } = string.Empty;

    public AcpAdapterDescriptor? FindAdapter(string? adapterId)
        => string.IsNullOrWhiteSpace(adapterId)
            ? null
            : Adapters.FirstOrDefault(adapter => string.Equals(adapter.Component.Id, adapterId, StringComparison.Ordinal));

    public AcpAdapterDescriptor? ResolveRecommendedAdapter()
        => FindAdapter(RecommendedAdapterId) ?? Adapters.FirstOrDefault();
}

/// <summary>
/// An ACP adapter paired with the launch template it requires.
/// </summary>
public sealed class AcpAdapterDescriptor
{
    public required AcpComponentDescriptor Component { get; init; }

    public required AcpLaunchTemplate LaunchTemplate { get; init; }
}
