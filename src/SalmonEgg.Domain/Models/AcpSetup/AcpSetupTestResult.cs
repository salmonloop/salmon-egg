namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// Result of testing a launch plan end to end. Carries the stage reached so a failure can be
/// attributed, and a remediation hint key the presentation layer localizes.
/// </summary>
public sealed class AcpSetupTestResult
{
    public required bool IsSuccess { get; init; }

    /// <summary>Stage reached. On success this is <see cref="AcpSetupTestStage.Completed"/>.</summary>
    public required AcpSetupTestStage Stage { get; init; }

    /// <summary>Agent-reported protocol version, when the handshake completed.</summary>
    public int? NegotiatedProtocolVersion { get; init; }

    /// <summary>Agent name reported by <c>initialize</c>, when available.</summary>
    public string? AgentName { get; init; }

    /// <summary>Raw failure detail (process error, protocol error, stderr excerpt).</summary>
    public string? ErrorDetail { get; init; }

    /// <summary>
    /// Localization key for the remediation hint matching <see cref="Stage"/>. Null when the failure
    /// has no actionable advice beyond <see cref="ErrorDetail"/>.
    /// </summary>
    public string? RemediationKey { get; init; }

    public static AcpSetupTestResult Success(int? protocolVersion, string? agentName)
        => new()
        {
            IsSuccess = true,
            Stage = AcpSetupTestStage.Completed,
            NegotiatedProtocolVersion = protocolVersion,
            AgentName = agentName
        };

    public static AcpSetupTestResult Failure(
        AcpSetupTestStage stage,
        string? errorDetail,
        string? remediationKey = null)
        => new()
        {
            IsSuccess = false,
            Stage = stage,
            ErrorDetail = errorDetail,
            RemediationKey = remediationKey
        };
}
