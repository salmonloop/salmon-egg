namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// Result of a one-click component install attempt. The wizard always re-probes after an install, so
/// this type reports the attempt only — never availability.
/// </summary>
public sealed class AcpComponentInstallResult
{
    public required string ComponentId { get; init; }

    public required bool IsSuccess { get; init; }

    /// <summary>Installer exit code, when the installer ran to completion.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Trailing installer output, retained for the failure surface.</summary>
    public string? Output { get; init; }

    /// <summary>Reason the installer could not run or did not succeed. Developer-facing English.</summary>
    public string? ErrorDetail { get; init; }

    /// <summary>
    /// Localization key for advice matching this failure, or null when the failure has none beyond
    /// <see cref="ErrorDetail"/>.
    /// </summary>
    /// <remarks>
    /// Present so a platform layer can say <em>what to do</em> without owning display text, the same
    /// contract <see cref="AcpSetupTestResult.RemediationKey"/> uses. Without it the only thing the wizard
    /// could show for a missing toolchain was the raw detail — which names the executable that was looked
    /// for rather than the toolchain the user has to install, and is untranslated.
    /// </remarks>
    public string? RemediationKey { get; init; }

    /// <summary>
    /// The toolchain whose absence caused this failure, when that is the cause. Substituted into the
    /// message <see cref="RemediationKey"/> resolves, so the advice names the toolchain rather than a
    /// package-manager executable the user may never have heard of.
    /// </summary>
    public string? MissingToolchainName { get; init; }

    public static AcpComponentInstallResult Success(string componentId, string? output)
        => new() { ComponentId = componentId, IsSuccess = true, ExitCode = 0, Output = output };

    public static AcpComponentInstallResult Failure(
        string componentId,
        int? exitCode,
        string? output,
        string? errorDetail,
        string? remediationKey = null,
        string? missingToolchainName = null)
        => new()
        {
            ComponentId = componentId,
            IsSuccess = false,
            ExitCode = exitCode,
            Output = output,
            ErrorDetail = errorDetail,
            RemediationKey = remediationKey,
            MissingToolchainName = missingToolchainName
        };
}
