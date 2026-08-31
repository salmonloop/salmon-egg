using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// What a probe learned about one component: whether it is usable, where it lives, and its version
/// when the component reports one.
/// </summary>
public sealed class AcpComponentProbeResult
{
    public required string ComponentId { get; init; }

    public required AcpComponentAvailability Availability { get; init; }

    /// <summary>Resolved absolute path when the probe located an executable.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// Every distinct executable the probed command matched, in PATH precedence order.
    /// </summary>
    /// <remarks>
    /// <see cref="ExecutablePath"/> is the first of these and the one a launch would run. More than one
    /// entry means the machine has shadowed installs, which the user may need to choose between — a
    /// second copy is invisible to a shell and to the launch plan, so the wizard has to say it exists.
    /// Empty or single-entry on an ordinary machine, so callers must not treat several as the norm.
    /// </remarks>
    public IReadOnlyList<string> ExecutableCandidates { get; init; } = Array.Empty<string>();

    /// <summary>True when more than one distinct install matched, so the choice is the user's.</summary>
    public bool HasMultipleCandidates => ExecutableCandidates.Count > 1;

    /// <summary>Version string as reported by the component, when it could be read.</summary>
    public string? Version { get; init; }

    /// <summary>Package-manager location when this component was found as a package.</summary>
    public string? PackageLocation { get; init; }

    /// <summary>
    /// Diagnostic detail for <see cref="AcpComponentAvailability.Missing"/> and
    /// <see cref="AcpComponentAvailability.Undetermined"/>. Never surfaced as the primary message.
    /// </summary>
    public string? Detail { get; init; }

    public bool IsUsable
        => Availability is AcpComponentAvailability.Installed or AcpComponentAvailability.BuiltIn;

    public static AcpComponentProbeResult BuiltIn(string componentId)
        => new() { ComponentId = componentId, Availability = AcpComponentAvailability.BuiltIn };

    public static AcpComponentProbeResult Missing(string componentId, string? detail = null)
        => new() { ComponentId = componentId, Availability = AcpComponentAvailability.Missing, Detail = detail };

    public static AcpComponentProbeResult Undetermined(string componentId, string? detail = null)
        => new() { ComponentId = componentId, Availability = AcpComponentAvailability.Undetermined, Detail = detail };

    public static AcpComponentProbeResult Installed(
        string componentId,
        string? executablePath,
        string? version,
        IReadOnlyList<string>? candidates = null,
        string? packageLocation = null)
        => new()
        {
            ComponentId = componentId,
            Availability = AcpComponentAvailability.Installed,
            ExecutablePath = executablePath,
            Version = version,
            PackageLocation = packageLocation,
            ExecutableCandidates = candidates
                ?? (executablePath is null ? Array.Empty<string>() : new[] { executablePath })
        };
}
