using SalmonEgg.Acp.Protocol;
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
    public void ResolveForHydration_WhenOnlyResumeIsSupported_UsesResume()
    {
        var capabilities = new AgentCapabilities(
            sessionCapabilities: new SessionCapabilities
            {
                Resume = new SessionResumeCapabilities()
            });

        var mode = AcpSessionRecoveryPolicy.ResolveForHydration(capabilities);

        Assert.Equal(AcpSessionRecoveryMode.Resume, mode);
        Assert.True(AcpSessionRecoveryPolicy.ExpectsHistoryReplayForHydration(mode));
        Assert.Same(SessionReplayFrom.Start, AcpSessionRecoveryPolicy.ResolveHydrationResumeReplayFrom(mode));
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
        // Resync callers use CreateResumeParams without a replay cursor (plain resume).
        // ResolveHydrationResumeReplayFrom is hydration-only and still maps Resume -> start.
    }

    [Fact]
    public void CreateResumeParams_WhenReplayFromProvided_PreservesCursor()
    {
        var context = new AcpRemoteSessionRecoveryContext("/tmp/project", default);

        var @params = AcpRemoteSessionRecoveryRequestFactory.CreateResumeParams(
            "remote-1",
            context,
            mcpServers: [],
            SessionReplayFrom.Start);

        Assert.Equal("remote-1", @params.SessionId);
        Assert.Equal("/tmp/project", @params.Cwd);
        Assert.Same(SessionReplayFrom.Start, @params.ReplayFrom);
    }

    [Fact]
    public void CreateResumeParams_WhenReplayFromOmitted_LeavesNull()
    {
        var context = new AcpRemoteSessionRecoveryContext("/tmp/project", default);

        var @params = AcpRemoteSessionRecoveryRequestFactory.CreateResumeParams(
            "remote-1",
            context,
            mcpServers: []);

        Assert.Null(@params.ReplayFrom);
    }
}
