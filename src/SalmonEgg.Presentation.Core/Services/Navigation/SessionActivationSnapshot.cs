using System;

namespace SalmonEgg.Presentation.Core.Services;

public sealed record SessionActivationSnapshot(
    string SessionId,
    string? ProjectId,
    long Version,
    SessionActivationPhase Phase,
    string? Reason = null,
    string? FailureMessage = null)
{
    public bool Matches(string sessionId)
        => !string.IsNullOrWhiteSpace(sessionId)
           && string.Equals(SessionId, sessionId, StringComparison.Ordinal);
}
