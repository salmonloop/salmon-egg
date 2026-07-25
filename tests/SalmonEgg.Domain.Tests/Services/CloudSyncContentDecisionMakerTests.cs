using SalmonEgg.Domain.Services;
using Xunit;

namespace SalmonEgg.Domain.Tests.Services;

public sealed class CloudSyncContentDecisionMakerTests
{
    [Fact]
    public void Decide_WhenLocalEqualsRemote_RefreshesBaseline()
    {
        var decision = CloudSyncContentDecisionMaker.Decide(new CloudSyncContentDecisionInput(
            LocalFingerprint: "abc",
            RemoteFingerprint: "abc",
            SyncedFingerprint: "old",
            BaselineKnown: true,
            FirstAdoptPolicy: CloudSyncFirstAdoptPolicy.RequireManual));

        Assert.Equal(CloudSyncContentAction.RefreshBaseline, decision.Action);
    }

    [Fact]
    public void Decide_WhenOnlyLocalDirty_Uploads()
    {
        var decision = CloudSyncContentDecisionMaker.Decide(new CloudSyncContentDecisionInput(
            LocalFingerprint: "local",
            RemoteFingerprint: "base",
            SyncedFingerprint: "base",
            BaselineKnown: true,
            FirstAdoptPolicy: CloudSyncFirstAdoptPolicy.RequireManual));

        Assert.Equal(CloudSyncContentAction.UploadLocal, decision.Action);
    }

    [Fact]
    public void Decide_WhenOnlyRemoteAdvanced_Restores()
    {
        var decision = CloudSyncContentDecisionMaker.Decide(new CloudSyncContentDecisionInput(
            LocalFingerprint: "base",
            RemoteFingerprint: "remote",
            SyncedFingerprint: "base",
            BaselineKnown: true,
            FirstAdoptPolicy: CloudSyncFirstAdoptPolicy.RequireManual));

        Assert.Equal(CloudSyncContentAction.RestoreRemote, decision.Action);
    }

    [Fact]
    public void Decide_WhenTrueConflict_FailsClosed()
    {
        var decision = CloudSyncContentDecisionMaker.Decide(new CloudSyncContentDecisionInput(
            LocalFingerprint: "local",
            RemoteFingerprint: "remote",
            SyncedFingerprint: "base",
            BaselineKnown: true,
            FirstAdoptPolicy: CloudSyncFirstAdoptPolicy.PreferRemote));

        Assert.Equal(CloudSyncContentAction.FailClosedConflict, decision.Action);
    }

    [Fact]
    public void Decide_WhenBaselineUnknownAndRequireManual_FailsClosed()
    {
        var decision = CloudSyncContentDecisionMaker.Decide(new CloudSyncContentDecisionInput(
            LocalFingerprint: "local",
            RemoteFingerprint: "remote",
            SyncedFingerprint: string.Empty,
            BaselineKnown: false,
            FirstAdoptPolicy: CloudSyncFirstAdoptPolicy.RequireManual));

        Assert.Equal(CloudSyncContentAction.FailClosedConflict, decision.Action);
    }

    [Fact]
    public void Decide_WhenBaselineUnknownAndPreferRemote_Restores()
    {
        var decision = CloudSyncContentDecisionMaker.Decide(new CloudSyncContentDecisionInput(
            LocalFingerprint: "local",
            RemoteFingerprint: "remote",
            SyncedFingerprint: string.Empty,
            BaselineKnown: false,
            FirstAdoptPolicy: CloudSyncFirstAdoptPolicy.PreferRemote));

        Assert.Equal(CloudSyncContentAction.RestoreRemote, decision.Action);
    }

    [Fact]
    public void Decide_WhenBaselineUnknownButContentConverged_RefreshesBaseline()
    {
        var decision = CloudSyncContentDecisionMaker.Decide(new CloudSyncContentDecisionInput(
            LocalFingerprint: "same",
            RemoteFingerprint: "same",
            SyncedFingerprint: string.Empty,
            BaselineKnown: false,
            FirstAdoptPolicy: CloudSyncFirstAdoptPolicy.RequireManual));

        Assert.Equal(CloudSyncContentAction.RefreshBaseline, decision.Action);
    }
}
