using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class AcpSessionRecoveryPolicyTests
{
    [Fact]
    public void ResolveForHydration_WhenLoadAndResumeAreSupported_PrefersLoad()
    {
        var capabilities = new AgentCapabilities(
            loadSession: true,
            sessionCapabilities: new SessionCapabilities
            {
                Resume = new SessionResumeCapabilities()
            });

        var mode = AcpSessionRecoveryPolicy.ResolveForHydration(capabilities);

        Assert.Equal(AcpSessionRecoveryMode.Load, mode);
    }

    [Fact]
    public void ResolveForResync_WhenLoadAndResumeAreSupported_PrefersResume()
    {
        var capabilities = new AgentCapabilities(
            loadSession: true,
            sessionCapabilities: new SessionCapabilities
            {
                Resume = new SessionResumeCapabilities()
            });

        var mode = AcpSessionRecoveryPolicy.ResolveForResync(capabilities);

        Assert.Equal(AcpSessionRecoveryMode.Resume, mode);
    }
}
