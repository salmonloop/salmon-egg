namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>Whether adding an installed toolchain directory to the user's persistent PATH succeeded.</summary>
public enum AcpPathRegistration
{
    /// <summary>The directory was added.</summary>
    Applied,

    /// <summary>The directory was already present, so no persistent environment was changed.</summary>
    AlreadyPresent,

    /// <summary>
    /// The toolchain installed successfully, but persisting PATH failed. This is non-fatal: the wizard's
    /// own disk scan still sees the toolchain, while a new terminal may need manual configuration.
    /// </summary>
    Failed
}

/// <summary>Result of a toolchain download, verification, unpack, and optional PATH registration.</summary>
public sealed class AcpToolchainInstallResult
{
    public required AcpToolchainRequirement Requirement { get; init; }

    public required bool IsSuccess { get; init; }

    /// <summary>The executable directory installed and verified, when successful.</summary>
    public string? InstalledBinDirectory { get; init; }

    public AcpPathRegistration? PathRegistration { get; init; }

    /// <summary>Trailing installer output retained for the UI's failure surface.</summary>
    public string? Output { get; init; }

    /// <summary>Developer-facing reason the installer could not complete.</summary>
    public string? ErrorDetail { get; init; }

    /// <summary>Localization key for actionable advice, when one exists.</summary>
    public string? RemediationKey { get; init; }

    public static AcpToolchainInstallResult Success(
        AcpToolchainRequirement requirement,
        string installedBinDirectory,
        AcpPathRegistration pathRegistration,
        string? output)
        => new()
        {
            Requirement = requirement,
            IsSuccess = true,
            InstalledBinDirectory = installedBinDirectory,
            PathRegistration = pathRegistration,
            Output = output
        };

    public static AcpToolchainInstallResult Failure(
        AcpToolchainRequirement requirement,
        string? errorDetail,
        string? output = null,
        string? remediationKey = null)
        => new()
        {
            Requirement = requirement,
            IsSuccess = false,
            ErrorDetail = errorDetail,
            Output = output,
            RemediationKey = remediationKey
        };
}
