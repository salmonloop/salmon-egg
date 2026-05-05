using System;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public class ConversationWarmReusePolicyTests
{
    [Fact]
    public void CanReuseRemoteWarmConversation_WhenAllFieldsMatch_ReturnsTrue()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            "SessionLoadCompleted",
            new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc));

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            runtime,
            binding,
            Connection(),
            hasReusableProjection: true);

        Assert.True(result);
    }

    [Fact]
    public void EvaluateRemoteWarmConversation_WhenAllFieldsMatch_ReturnsReusableWithoutDenialReason()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            "SessionLoadCompleted",
            new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc));

        var decision = ConversationWarmReusePolicy.EvaluateRemoteWarmConversation(
            runtime,
            binding,
            Connection(),
            hasReusableProjection: true);

        Assert.True(decision.CanReuse);
        Assert.Null(decision.DenialReason);
    }

    [Fact]
    public void EvaluateRemoteWarmConversation_WhenConnectionProfileDiffers_ReturnsProfileMismatchDenial()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            "SessionLoadCompleted",
            DateTime.UtcNow);

        var decision = ConversationWarmReusePolicy.EvaluateRemoteWarmConversation(
            runtime,
            binding,
            Connection(profileId: "profile-2"),
            hasReusableProjection: true);

        Assert.False(decision.CanReuse);
        Assert.Equal("ConnectionProfileMismatch", decision.DenialReason);
    }

    [Fact]
    public void CanReuseRemoteWarmConversation_WhenWarmReasonIsProfileReconnectReuse_ReturnsTrue()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            ConversationRuntimeReasons.WarmReuseAfterProfileReconnect,
            new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc));

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            runtime,
            binding,
            Connection(),
            hasReusableProjection: true);

        var denialReason = ConversationWarmReusePolicy.GetWarmReuseDenialReason(
            runtime,
            binding,
            Connection(),
            hasReusableProjection: true);

        Assert.True(result);
        Assert.Null(denialReason);
    }

    [Fact]
    public void HasAuthoritativeWarmRuntime_WhenReasonIsPolicyConstant_ReturnsTrue()
    {
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            ConversationRuntimeReasons.WarmReuseAfterProfileReconnect,
            DateTime.UtcNow);

        Assert.True(ConversationWarmReusePolicy.HasAuthoritativeWarmRuntime(runtime));
    }

    [Fact]
    public void CanReuseRemoteWarmConversation_WhenConnectionInstanceIdDiffers_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            null,
            DateTime.UtcNow);

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            runtime,
            binding,
            Connection(connectionId: "conn-2"),
            hasReusableProjection: true);

        Assert.False(result);
    }

    [Fact]
    public void CanReuseRemoteWarmConversation_WhenConnectionProfileDiffers_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            "SessionLoadCompleted",
            DateTime.UtcNow);
        var currentConnection = new ConversationWarmReuseConnectionIdentity("profile-2", "conn-1");

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            runtime,
            binding,
            currentConnection,
            hasReusableProjection: true);
        var denialReason = ConversationWarmReusePolicy.GetWarmReuseDenialReason(
            runtime,
            binding,
            currentConnection,
            hasReusableProjection: true);

        Assert.False(result);
        Assert.Equal("ConnectionProfileMismatch", denialReason);
    }

    [Fact]
    public void CanReuseRemoteWarmConversation_WhenWarmReasonIsNotAuthoritative_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            "SeedWarm",
            DateTime.UtcNow);

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            runtime,
            binding,
            Connection(),
            hasReusableProjection: true);

        Assert.False(result);
    }

    [Fact]
    public void CanReuseRemoteWarmConversation_WhenProfileMismatch_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-2");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            null,
            DateTime.UtcNow);

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            runtime,
            binding,
            Connection(profileId: "profile-2"),
            hasReusableProjection: true);

        Assert.False(result);
    }

    [Fact]
    public void CanReuseRemoteWarmConversation_WhenRemoteSessionIdDiffers_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-2", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            null,
            DateTime.UtcNow);

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            runtime,
            binding,
            Connection(),
            hasReusableProjection: true);

        Assert.False(result);
    }

    [Fact]
    public void CanReuseRemoteWarmConversation_WhenRuntimeIsNull_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            null,
            binding,
            Connection(),
            hasReusableProjection: true);

        Assert.False(result);
    }

    [Fact]
    public void CanReuseRemoteWarmConversation_WhenBindingIsNull_ReturnsFalse()
    {
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            null,
            DateTime.UtcNow);

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            runtime,
            null,
            Connection(),
            hasReusableProjection: true);

        Assert.False(result);
    }

    [Fact]
    public void CanReuseRemoteWarmConversation_WhenRuntimeIsNotWarm_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.RemoteHydrating,
            "conn-1",
            "remote-1",
            "profile-1",
            null,
            DateTime.UtcNow);

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            runtime,
            binding,
            Connection(),
            hasReusableProjection: true);

        Assert.False(result);
    }

    [Fact]
    public void CanReuseRemoteWarmConversation_WhenCurrentConnectionInstanceIdIsEmpty_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            null,
            DateTime.UtcNow);

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            runtime,
            binding,
            Connection(connectionId: string.Empty),
            hasReusableProjection: true);

        Assert.False(result);
    }

    [Fact]
    public void GetWarmReuseDenialReason_WhenConnectionInstanceIdDiffers_ReturnsConnectionInstanceIdMismatch()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            "SessionLoadCompleted",
            DateTime.UtcNow);

        var reason = ConversationWarmReusePolicy.GetWarmReuseDenialReason(
            runtime, binding, Connection(connectionId: "conn-2"), hasReusableProjection: true);

        Assert.Equal("ConnectionInstanceIdMismatch", reason);
    }

    [Fact]
    public void CanReuseRemoteWarmConversation_WhenReusableProjectionIsMissing_ReturnsFalse()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            "SessionLoadCompleted",
            DateTime.UtcNow);

        var result = ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
            runtime,
            binding,
            Connection(),
            hasReusableProjection: false);

        Assert.False(result);
    }

    [Fact]
    public void GetWarmReuseDenialReason_WhenReusableProjectionIsMissing_ReturnsProjectionNotReady()
    {
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");
        var runtime = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            "conn-1",
            "remote-1",
            "profile-1",
            "SessionLoadCompleted",
            DateTime.UtcNow);

        var reason = ConversationWarmReusePolicy.GetWarmReuseDenialReason(
            runtime,
            binding,
            Connection(),
            hasReusableProjection: false);

        Assert.Equal("ProjectionNotReady", reason);
    }

    private static ConversationWarmReuseConnectionIdentity Connection(
        string? profileId = "profile-1",
        string? connectionId = "conn-1")
        => new(profileId, connectionId);
}
