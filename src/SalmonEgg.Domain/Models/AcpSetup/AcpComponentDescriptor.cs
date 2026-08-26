using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// One installable ACP component: either the agent runtime the user already has on the machine, or
/// the ACP adapter that fronts it. Detection and installation are declared per component so the
/// wizard never needs component-specific branches.
/// </summary>
public sealed class AcpComponentDescriptor
{
    /// <summary>Stable identifier used by persistence, telemetry, and probe correlation.</summary>
    public required string Id { get; init; }

    /// <summary>Human-facing name. Vendor product names are not localized.</summary>
    public required string DisplayName { get; init; }

    public AcpDistributionKind Distribution { get; init; } = AcpDistributionKind.Npx;

    public AcpComponentDetectionMode DetectionMode { get; init; } = AcpComponentDetectionMode.ExecutableOnPath;

    /// <summary>
    /// Executable probed for <see cref="AcpComponentDetectionMode.ExecutableOnPath"/>, and the
    /// launcher whose presence gates package detection for the global-package modes.
    /// </summary>
    public string ProbeCommand { get; init; } = string.Empty;

    /// <summary>Arguments appended to <see cref="ProbeCommand"/> when asking for a version.</summary>
    public IReadOnlyList<string> ProbeVersionArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Package coordinate the wizard installs for package-manager distributions. Detection may use the
    /// installed executable instead: a package coordinate identifies one distribution source, not the
    /// component's runtime identity.
    /// </summary>
    public string PackageId { get; init; } = string.Empty;

    /// <summary>Documentation offered when automatic installation is unavailable or fails.</summary>
    public Uri? InstallDocumentation { get; init; }

    /// <summary>
    /// True when the wizard can install this component itself. Binary distributions are
    /// download + checksum + unpack flows the wizard deliberately does not automate.
    /// </summary>
    public bool SupportsAutomaticInstall
        => Distribution is AcpDistributionKind.Npx or AcpDistributionKind.Uvx
            && !string.IsNullOrWhiteSpace(PackageId);

    /// <summary>True when nothing has to exist on the machine for this component to be usable.</summary>
    public bool IsBuiltIn => Distribution == AcpDistributionKind.BuiltIn;
}
