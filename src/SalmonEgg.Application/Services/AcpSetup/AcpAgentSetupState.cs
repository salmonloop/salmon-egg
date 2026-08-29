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

    /// <summary>
    /// Whether the toolchain the runtime would install through exists here. Null when the runtime needs
    /// no toolchain, which is not the same as one being absent.
    /// </summary>
    public AcpToolchainProbeResult? RuntimeToolchain { get; init; }

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

    /// <summary>
    /// User-supplied paths for commands the catalog names by executable name.
    /// </summary>
    /// <remarks>
    /// Carried on the draft rather than applied only while probing, so the plan that is tested and the
    /// plan that is persisted are the same one. An override honoured during detection alone would let a
    /// profile pass its connection test and then fail every launch, which is worse than never having
    /// offered the override.
    /// </remarks>
    public AcpCommandOverrides CommandOverrides { get; init; } = AcpCommandOverrides.Empty;

    public AcpLaunchPlan BuildLaunchPlan()
        => AcpLaunchPlanBuilder.Build(Adapter.LaunchTemplate, ParameterValues, CommandOverrides);
}
