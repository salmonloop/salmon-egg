using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class RemoteConversationWorkspaceSnapshotPolicyTests
{
    [Fact]
    public void CanReuseWarmProjectionSnapshot_WhenConversationIsLocal_ReturnsTrue()
    {
        var binding = new ConversationBindingSlice("conv-1", null, null);
        var snapshot = CreateSnapshot();

        var result = RemoteConversationWorkspaceSnapshotPolicy.CanReuseWarmProjectionSnapshot(
            binding,
            snapshot,
            ConversationWorkspaceSnapshotOrigin.Restored);

        Assert.True(result);
    }

    [Fact]
    public void CanReuseWarmProjectionSnapshot_WhenRemoteSnapshotWasOnlyRestored_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var snapshot = CreateSnapshot();

        var result = RemoteConversationWorkspaceSnapshotPolicy.CanReuseWarmProjectionSnapshot(
            binding,
            snapshot,
            ConversationWorkspaceSnapshotOrigin.Restored);

        Assert.False(result);
    }

    [Fact]
    public void HasAuthoritativeRemoteRuntimeProjection_WhenRemoteSnapshotCameFromRuntimeProjection_ReturnsTrue()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var snapshot = CreateSnapshot();

        var result = RemoteConversationWorkspaceSnapshotPolicy.HasAuthoritativeRemoteRuntimeProjection(
            binding,
            snapshot,
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);

        Assert.True(result);
    }

    [Theory]
    [InlineData(ConversationWorkspaceSnapshotOrigin.Restored, false)]
    [InlineData(ConversationWorkspaceSnapshotOrigin.RuntimeProjection, true)]
    public void CanRestoreCachedTranscriptAfterInterruptedHydration_ForRemoteConversation_RequiresLiveProjectionEvidence(
        ConversationWorkspaceSnapshotOrigin origin,
        bool expected)
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtimeState = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            "SessionLoadCompleted",
            DateTime.UtcNow);
        var snapshot = CreateSnapshot();

        var result = RemoteConversationWorkspaceSnapshotPolicy.CanRestoreCachedTranscriptAfterInterruptedHydration(
            binding,
            snapshot,
            origin,
            currentConnectionInstanceId: "conn-1");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CanRestoreCachedTranscriptAfterInterruptedHydration_WhenConversationIsLocal_ReturnsTrue()
    {
        var binding = new ConversationBindingSlice("conv-1", null, null);
        var snapshot = CreateSnapshot();

        var result = RemoteConversationWorkspaceSnapshotPolicy.CanRestoreCachedTranscriptAfterInterruptedHydration(
            binding,
            snapshot,
            ConversationWorkspaceSnapshotOrigin.Restored,
            currentConnectionInstanceId: null);

        Assert.True(result);
    }

    [Fact]
    public void CanRestoreCachedTranscriptAfterInterruptedHydration_WhenRemoteWarmRuntimeConnectionChanged_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtimeState = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-old",
            "remote-1",
            "profile-1",
            "SessionLoadCompleted",
            DateTime.UtcNow);
        var snapshot = CreateSnapshot();

        var result = RemoteConversationWorkspaceSnapshotPolicy.CanRestoreCachedTranscriptAfterInterruptedHydration(
            binding,
            snapshot,
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection,
            currentConnectionInstanceId: "conn-new");

        Assert.False(result);
    }

    [Fact]
    public void CanRestoreCachedTranscriptAfterAuthoritativeHydration_WhenRemoteSnapshotIsRuntimeProjection_ReturnsTrue()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var snapshot = CreateSnapshot();

        var result = RemoteConversationWorkspaceSnapshotPolicy.CanRestoreCachedTranscriptAfterAuthoritativeHydration(
            binding,
            snapshot,
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection,
            currentConnectionInstanceId: "conn-1");

        Assert.True(result);
    }

    [Fact]
    public void CanRestoreCachedTranscriptAfterAuthoritativeHydration_WhenRemoteSnapshotWasOnlyRestored_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var snapshot = CreateSnapshot();

        var result = RemoteConversationWorkspaceSnapshotPolicy.CanRestoreCachedTranscriptAfterAuthoritativeHydration(
            binding,
            snapshot,
            ConversationWorkspaceSnapshotOrigin.Restored,
            currentConnectionInstanceId: "conn-1");

        Assert.False(result);
    }

    [Fact]
    public void ResolveCurrentConnectionInstanceId_WhenStoreHasForegroundConnection_ReturnsStoreIdentity()
    {
        var connectionState = ChatConnectionState.Empty with
        {
            ConnectionInstanceId = "conn-foreground",
            ForegroundTransportProfileId = "profile-other"
        };

        var result = ConversationProjectionRestoreConnectionPolicy.ResolveCurrentConnectionInstanceId(
            connectionState,
            fallbackConnectionInstanceId: "conn-viewmodel");

        Assert.Equal("conn-foreground", result);
    }

    [Fact]
    public void ResolveCurrentConnectionInstanceId_WhenStoreIdentityIsMissing_ReturnsFallbackIdentity()
    {
        var connectionState = ChatConnectionState.Empty with
        {
            ConnectionInstanceId = null,
            ForegroundTransportProfileId = "profile-other"
        };

        var result = ConversationProjectionRestoreConnectionPolicy.ResolveCurrentConnectionInstanceId(
            connectionState,
            fallbackConnectionInstanceId: "conn-viewmodel");

        Assert.Equal("conn-viewmodel", result);
    }

    private static ConversationWorkspaceSnapshot CreateSnapshot()
        => new(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1");
}
