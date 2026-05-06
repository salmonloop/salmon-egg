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
    public static AcpSessionRecoveryMode Resolve(AgentCapabilities? capabilities)
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
}
