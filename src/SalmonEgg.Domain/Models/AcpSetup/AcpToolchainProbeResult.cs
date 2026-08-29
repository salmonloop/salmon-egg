namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// Whether the toolchain a distribution installs through is present on this machine.
/// </summary>
/// <remarks>
/// Three states rather than a boolean, for the same reason component detection has three: a probe that
/// could not run has not established absence. Telling a user their toolchain is missing when the wizard
/// merely failed to look would send them to install something they already have.
/// </remarks>
public enum AcpToolchainAvailability
{
    /// <summary>The toolchain's package manager resolved to an executable.</summary>
    Available,

    /// <summary>The package manager was searched for and not found.</summary>
    Missing,

    /// <summary>The search could not be performed on this platform.</summary>
    Undetermined
}

/// <summary>
/// What a probe learned about the toolchain a package-manager distribution needs.
/// </summary>
/// <remarks>
/// Separate from <see cref="AcpComponentProbeResult"/> because it answers a different question about a
/// different subject: a component probe says whether the agent is here, this says whether the machine can
/// install one at all. Collapsing them would make a missing toolchain read as a missing agent, which is
/// the confusion this type exists to prevent.
/// </remarks>
public sealed class AcpToolchainProbeResult
{
    private AcpToolchainProbeResult(
        AcpToolchainRequirement requirement,
        AcpToolchainAvailability availability,
        string? managerPath,
        string? detail)
    {
        Requirement = requirement;
        Availability = availability;
        ManagerPath = managerPath;
        Detail = detail;
    }

    /// <summary>The toolchain this result is about.</summary>
    public AcpToolchainRequirement Requirement { get; }

    public AcpToolchainAvailability Availability { get; }

    /// <summary>Absolute path of the package manager, when one was resolved.</summary>
    public string? ManagerPath { get; }

    /// <summary>
    /// Diagnostic detail naming what was looked for. Developer-facing; never the primary message.
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// True when an install may be attempted.
    /// </summary>
    /// <remarks>
    /// <see cref="AcpToolchainAvailability.Undetermined"/> counts as usable: the wizard offers the attempt
    /// and lets the installer report what really happened, rather than withholding a button over a
    /// question it could not answer. Only a positive "not here" withholds it.
    /// </remarks>
    public bool AllowsInstallAttempt => Availability is not AcpToolchainAvailability.Missing;

    /// <summary>True when the toolchain was searched for and positively not found.</summary>
    public bool IsMissing => Availability is AcpToolchainAvailability.Missing;

    public static AcpToolchainProbeResult Available(AcpToolchainRequirement requirement, string? managerPath)
        => new(requirement, AcpToolchainAvailability.Available, managerPath, detail: null);

    public static AcpToolchainProbeResult Missing(AcpToolchainRequirement requirement, string? detail = null)
        => new(requirement, AcpToolchainAvailability.Missing, managerPath: null, detail);

    public static AcpToolchainProbeResult Undetermined(AcpToolchainRequirement requirement, string? detail = null)
        => new(requirement, AcpToolchainAvailability.Undetermined, managerPath: null, detail);
}
