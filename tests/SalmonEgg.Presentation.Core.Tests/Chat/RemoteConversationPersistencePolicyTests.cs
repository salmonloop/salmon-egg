using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class RemoteConversationPersistencePolicyTests
{
    [Fact]
    public void IsRemoteBacked_WhenBothMissing_ReturnsFalse()
    {
        Assert.False(RemoteConversationPersistencePolicy.IsRemoteBacked(null, null));
    }

    [Fact]
    public void IsRemoteBacked_WhenBothBlank_ReturnsFalse()
    {
        Assert.False(RemoteConversationPersistencePolicy.IsRemoteBacked("   ", "\t"));
    }

    [Fact]
    public void IsRemoteBacked_WhenRemoteSessionIdPresent_ReturnsTrue()
    {
        Assert.True(RemoteConversationPersistencePolicy.IsRemoteBacked("remote-1", null));
    }

    [Fact]
    public void IsRemoteBacked_WhenOnlyBoundProfilePresent_ReturnsTrue()
    {
        // A profile binding alone makes the conversation remote-backed, so the runtime
        // content must not be cached as if it were local truth.
        Assert.True(RemoteConversationPersistencePolicy.IsRemoteBacked(null, "profile-1"));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("remote-1", null, false)]
    [InlineData(null, "profile-1", false)]
    [InlineData("remote-1", "profile-1", false)]
    public void ShouldPersistRuntimeContent_IsInverseOfRemoteBacked(string? remoteSessionId, string? profileId, bool expected)
    {
        Assert.Equal(expected, RemoteConversationPersistencePolicy.ShouldPersistRuntimeContent(remoteSessionId, profileId));
        Assert.Equal(!expected, RemoteConversationPersistencePolicy.IsRemoteBacked(remoteSessionId, profileId));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("remote-1", null, false)]
    [InlineData(null, "profile-1", false)]
    public void ShouldRestoreRuntimeContent_IsInverseOfRemoteBacked(string? remoteSessionId, string? profileId, bool expected)
    {
        Assert.Equal(expected, RemoteConversationPersistencePolicy.ShouldRestoreRuntimeContent(remoteSessionId, profileId));
        Assert.Equal(!expected, RemoteConversationPersistencePolicy.IsRemoteBacked(remoteSessionId, profileId));
    }
}
