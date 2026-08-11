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
    /// Stable V1 cold hydration requires <c>session/load</c>, because V1
    /// <c>session/resume</c> reattaches without replaying conversation history.
    /// </summary>
    public static bool ExpectsHistoryReplayForHydration(AcpSessionRecoveryMode recoveryMode)
        => recoveryMode == AcpSessionRecoveryMode.Load;
}
