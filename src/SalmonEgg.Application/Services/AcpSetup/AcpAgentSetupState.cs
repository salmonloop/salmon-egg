using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Application.Services.AcpSetup;

/// <summary>
/// Detection outcome for one catalog agent: the runtime probe plus the probe of the adapter the wizard
/// would use. Adapter detection is only meaningful once a runtime is present, so it is optional here.
/// </summary>
public sealed class AcpAgentDetectionState
{
    public required AcpAgentDescriptor Agent { get; init; }

    public required AcpComponentProbeResult Runtime { get; init; }

    /// <summary>Probe of the recommended adapter, or null when the adapter has not been probed yet.</summary>
    public AcpComponentProbeResult? Adapter { get; init; }

    /// <summary>True when the agent can be configured without installing anything.</summary>
    public bool IsReady => Runtime.IsUsable && Adapter?.IsUsable == true;
}

/// <summary>
/// The configuration a completed wizard produces, before it is persisted.
/// </summary>
public sealed class AcpSetupDraft
{
    public required AcpAgentDescriptor Agent { get; init; }

    public required AcpAdapterDescriptor Adapter { get; init; }

    public required IReadOnlyDictionary<string, string> ParameterValues { get; init; }

    /// <summary>Profile name the configuration is saved under.</summary>
    public required string ProfileName { get; init; }

    public AcpLaunchPlan BuildLaunchPlan()
        => AcpLaunchPlanBuilder.Build(Adapter.LaunchTemplate, ParameterValues);
}
