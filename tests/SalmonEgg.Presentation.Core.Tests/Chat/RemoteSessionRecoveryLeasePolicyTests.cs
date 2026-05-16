using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public class RemoteSessionRecoveryLeasePolicyTests
{
    [Fact]
    public void Decide_WhenSameLeaseAlreadyExists_ReusesExistingLease()
    {
        var requested = Lease(remoteSessionId: "remote-a");
        var decision = RemoteSessionRecoveryLeasePolicy.Decide(
            requested,
            [requested]);

        Assert.Equal(RemoteSessionRecoveryLeaseDecisionKind.ReuseExisting, decision.Kind);
        Assert.False(decision.ShouldStartNew);
        Assert.True(decision.ShouldReuseExisting);
        Assert.Equal(requested, decision.ExistingLeaseToReuse);
        Assert.Empty(decision.ConflictingLeasesToCancel);
    }

    [Fact]
    public void Decide_WhenDifferentRemoteSessionUsesSameConnection_AllowsConcurrentLease()
    {
        var existing = Lease(remoteSessionId: "remote-a");
        var requested = Lease(remoteSessionId: "remote-b");

        var decision = RemoteSessionRecoveryLeasePolicy.Decide(
            requested,
            [existing]);

        Assert.Equal(RemoteSessionRecoveryLeaseDecisionKind.StartNew, decision.Kind);
        Assert.True(decision.ShouldStartNew);
        Assert.False(decision.ShouldReuseExisting);
        Assert.Null(decision.ExistingLeaseToReuse);
        Assert.Empty(decision.ConflictingLeasesToCancel);
    }

    [Fact]
    public void Decide_WhenSameRemoteSessionUsesDifferentCwd_ReusesExistingLease()
    {
        var existing = Lease(remoteSessionId: "remote-a", cwd: "C:\\repo-a");
        var requested = Lease(remoteSessionId: "remote-a", cwd: "C:\\repo-b");

        var decision = RemoteSessionRecoveryLeasePolicy.Decide(
            requested,
            [existing]);

        Assert.Equal(RemoteSessionRecoveryLeaseDecisionKind.ReuseExisting, decision.Kind);
        Assert.True(decision.ShouldReuseExisting);
        Assert.Equal(existing, decision.ExistingLeaseToReuse);
        Assert.Empty(decision.ConflictingLeasesToCancel);
    }

    [Fact]
    public void Decide_WhenSameRemoteSessionUsesDifferentRecoveryMode_ReusesExistingLease()
    {
        var existing = Lease(remoteSessionId: "remote-a", recoveryMode: AcpSessionRecoveryMode.Load);
        var requested = Lease(remoteSessionId: "remote-a", recoveryMode: AcpSessionRecoveryMode.Resume);

        var decision = RemoteSessionRecoveryLeasePolicy.Decide(
            requested,
            [existing]);

        Assert.Equal(RemoteSessionRecoveryLeaseDecisionKind.ReuseExisting, decision.Kind);
        Assert.True(decision.ShouldReuseExisting);
        Assert.Equal(existing, decision.ExistingLeaseToReuse);
        Assert.Empty(decision.ConflictingLeasesToCancel);
    }

    [Fact]
    public void Decide_WhenSameRemoteSessionUsesDifferentConnectionInstance_CancelsExistingLease()
    {
        var existing = Lease(remoteSessionId: "remote-a", connectionInstanceId: "conn-old");
        var requested = Lease(remoteSessionId: "remote-a", connectionInstanceId: "conn-new");

        var decision = RemoteSessionRecoveryLeasePolicy.Decide(
            requested,
            [existing]);

        Assert.Equal(RemoteSessionRecoveryLeaseDecisionKind.StartNew, decision.Kind);
        Assert.True(decision.ShouldStartNew);
        Assert.Equal([existing], decision.ConflictingLeasesToCancel);
    }

    [Fact]
    public void Decide_WhenDifferentProfileUsesSameRemoteSession_AllowsConcurrentLease()
    {
        var existing = Lease(remoteSessionId: "remote-a", profileId: "profile-a");
        var requested = Lease(remoteSessionId: "remote-a", profileId: "profile-b");

        var decision = RemoteSessionRecoveryLeasePolicy.Decide(
            requested,
            [existing]);

        Assert.Equal(RemoteSessionRecoveryLeaseDecisionKind.StartNew, decision.Kind);
        Assert.True(decision.ShouldStartNew);
        Assert.Empty(decision.ConflictingLeasesToCancel);
    }

    [Fact]
    public void Decide_WhenConnectionIdentityBecomesAuthoritativeForSameRemoteSession_CancelsUnknownConnectionLease()
    {
        var existing = Lease(remoteSessionId: "remote-a", connectionInstanceId: null);
        var requested = Lease(remoteSessionId: "remote-a", connectionInstanceId: "conn-1");

        var decision = RemoteSessionRecoveryLeasePolicy.Decide(
            requested,
            [existing]);

        Assert.Equal(RemoteSessionRecoveryLeaseDecisionKind.StartNew, decision.Kind);
        Assert.True(decision.ShouldStartNew);
        Assert.Equal([existing], decision.ConflictingLeasesToCancel);
    }

    private static RemoteSessionRecoveryLeaseKey Lease(
        AcpSessionRecoveryMode recoveryMode = AcpSessionRecoveryMode.Load,
        string? profileId = "profile-1",
        string? connectionInstanceId = "conn-1",
        string remoteSessionId = "remote-1",
        string cwd = "C:\\repo")
        => new(
            recoveryMode,
            profileId,
            connectionInstanceId,
            remoteSessionId,
            cwd);
}
