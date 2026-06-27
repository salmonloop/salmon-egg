using SalmonEgg.Domain.Models.Protocol;

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
}
