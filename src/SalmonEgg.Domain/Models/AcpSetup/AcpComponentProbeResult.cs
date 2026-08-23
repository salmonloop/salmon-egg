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

    /// <summary>Version string as reported by the component, when it could be read.</summary>
    public string? Version { get; init; }

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

    public static AcpComponentProbeResult Installed(string componentId, string? executablePath, string? version)
        => new()
        {
            ComponentId = componentId,
            Availability = AcpComponentAvailability.Installed,
            ExecutablePath = executablePath,
            Version = version
        };
}
