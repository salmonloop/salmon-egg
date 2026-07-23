using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public enum AcpSessionRecoveryMode
{
    None,
    Load,
    Resume
}

public static class AcpSessionRecoveryPolicy
{
    public static AcpSessionRecoveryMode ResolveForHydration(AgentCapabilities? capabilities)
    {
        if (capabilities?.SupportsSessionLoading == true)
        {
            return AcpSessionRecoveryMode.Load;
        }

        if (capabilities?.SupportsSessionResume == true)
        {
            return AcpSessionRecoveryMode.Resume;
        }

        return AcpSessionRecoveryMode.None;
    }

    public static AcpSessionRecoveryMode ResolveForResync(AgentCapabilities? capabilities)
    {
        if (capabilities?.SupportsSessionResume == true)
        {
            return AcpSessionRecoveryMode.Resume;
        }

        if (capabilities?.SupportsSessionLoading == true)
        {
            return AcpSessionRecoveryMode.Load;
        }

        return AcpSessionRecoveryMode.None;
    }

    /// <summary>
    /// Cold hydration always needs full history when recovery is available:
    /// V1 session/load, or V2 session/resume with <c>replayFrom: { type: "start" }</c>.
    /// Resync uses plain resume and must not call this helper.
    /// </summary>
    public static bool ExpectsHistoryReplayForHydration(AcpSessionRecoveryMode recoveryMode)
        => recoveryMode is AcpSessionRecoveryMode.Load or AcpSessionRecoveryMode.Resume;

    /// <summary>
    /// Hydration resume always requests full history via the official V2 start cursor.
    /// </summary>
    public static SessionReplayFrom? ResolveHydrationResumeReplayFrom(AcpSessionRecoveryMode recoveryMode)
        => recoveryMode == AcpSessionRecoveryMode.Resume
            ? SessionReplayFrom.Start
            : null;
}
