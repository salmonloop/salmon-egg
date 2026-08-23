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

    /// <summary>Reason the installer could not run or did not succeed.</summary>
    public string? ErrorDetail { get; init; }

    public static AcpComponentInstallResult Success(string componentId, string? output)
        => new() { ComponentId = componentId, IsSuccess = true, ExitCode = 0, Output = output };

    public static AcpComponentInstallResult Failure(
        string componentId,
        int? exitCode,
        string? output,
        string? errorDetail)
        => new()
        {
            ComponentId = componentId,
            IsSuccess = false,
            ExitCode = exitCode,
            Output = output,
            ErrorDetail = errorDetail
        };
}
