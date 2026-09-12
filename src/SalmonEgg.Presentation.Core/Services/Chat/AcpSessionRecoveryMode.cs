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
    public static AcpSessionRecoveryMode ResolveForHydration(AgentCapabilities? capabilities, int protocolVersion = AcpProtocolVersion.Default)
    {
        if (protocolVersion == AcpProtocolVersion.V2)
        {
            // The stable Load intent asks for authoritative history. The SDK translates it to
            // V2 session/resume with replayFrom:start under the negotiated version.
            return capabilities?.SupportsSessionResume == true ? AcpSessionRecoveryMode.Load : AcpSessionRecoveryMode.None;
        }
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
