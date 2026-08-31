using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class AcpConnectionSessionRegistryTests
{
    [Fact]
    public void Upsert_WhenProfileIsNew_IndexesSessionByProfileAndService()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var session = CreateSession("profile-a");

        var replaced = registry.Upsert(session);

        Assert.Null(replaced);
        Assert.True(registry.TryGetByProfile("profile-a", out var stored));
        Assert.Same(session.Service, stored.Service);
        Assert.True(registry.TryGetProfileId(session.Service, out var profileId));
        Assert.Equal("profile-a", profileId);
    }

    [Fact]
    public void Upsert_WhenSessionHasNoService_ThrowsArgumentNullException()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var session = new AcpConnectionSession(
            "profile-a",
            null!,
            new InitializeResponse(),
            new AcpConnectionReuseKey(TransportType.Stdio, string.Empty, string.Empty, string.Empty));

        var exception = Assert.Throws<ArgumentNullException>(() => registry.Upsert(session));

        Assert.Equal("Service", exception.ParamName);
        Assert.Empty(registry.GetSnapshot());
    }

    [Fact]
    public void Upsert_WhenProfileChangesService_RemovesOldServiceIndexAndReturnsReplacedSession()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var original = CreateSession("profile-a");
        var replacement = CreateSession("profile-a");
        registry.Upsert(original);

        var replaced = registry.Upsert(replacement);

        Assert.Same(original.Service, replaced?.Service);
        Assert.False(registry.TryGetProfileId(original.Service, out _));
        Assert.True(registry.TryGetProfileId(replacement.Service, out var profileId));
        Assert.Equal("profile-a", profileId);
        Assert.True(registry.TryGetByProfile("profile-a", out var stored));
        Assert.Same(replacement.Service, stored.Service);
    }

    [Fact]
    public void Upsert_WhenServiceMovesToAnotherProfile_RemovesDisplacedProfileAndPublishesConsistentEvents()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var service = CreateAdapter();
        var events = new List<(string ProfileId, bool IsConnected)>();
        registry.ProfileConnectionChanged += (profileId, isConnected) => events.Add((profileId, isConnected));
        registry.Upsert(CreateSession("profile-a", service));
        events.Clear();

        registry.Upsert(CreateSession("profile-b", service));

        Assert.False(registry.TryGetByProfile("profile-a", out _));
        Assert.True(registry.TryGetByProfile("profile-b", out var stored));
        Assert.Same(service, stored.Service);
        Assert.True(registry.TryGetProfileId(service, out var profileId));
        Assert.Equal("profile-b", profileId);
        Assert.Equal(
            new[] { ("profile-a", false), ("profile-b", true) },
            events);
    }

    [Fact]
    public void RemoveByProfile_WhenSessionExists_RemovesBothIndexes()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var session = CreateSession("profile-a");
        registry.Upsert(session);

        var removed = registry.RemoveByProfile("profile-a");

        Assert.True(removed);
        Assert.False(registry.TryGetByProfile("profile-a", out _));
        Assert.False(registry.TryGetProfileId(session.Service, out _));
    }

    [Fact]
    public void RemoveByService_WhenSessionExists_RemovesBothIndexesAndReturnsProfileId()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var session = CreateSession("profile-a");
        registry.Upsert(session);

        var removed = registry.RemoveByService(session.Service, out var profileId);

        Assert.True(removed);
        Assert.Equal("profile-a", profileId);
        Assert.False(registry.TryGetByProfile("profile-a", out _));
        Assert.False(registry.TryGetProfileId(session.Service, out _));
    }

    [Fact]
    public void RemoveWhere_WhenSomeSessionsMatch_RemovesOnlyTheirServiceIndexes()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var removedSession = CreateSession("remove");
        var retainedSession = CreateSession("retain");
        registry.Upsert(removedSession);
        registry.Upsert(retainedSession);

        var removed = registry.RemoveWhere(session => session.ProfileId == "remove");

        Assert.Collection(removed, session => Assert.Same(removedSession.Service, session.Service));
        Assert.False(registry.TryGetProfileId(removedSession.Service, out _));
        Assert.True(registry.TryGetProfileId(retainedSession.Service, out var retainedProfileId));
        Assert.Equal("retain", retainedProfileId);
        Assert.True(registry.TryGetByProfile("retain", out _));
    }

    [Fact]
    public void Touch_WhenSessionExists_UpdatesSnapshotWithoutChangingServiceIndex()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var session = CreateSession("profile-a") with { LastUsedUtc = DateTime.UnixEpoch };
        var updatedAt = DateTime.UnixEpoch.AddHours(1);
        registry.Upsert(session);

        var touched = registry.Touch("profile-a", updatedAt);

        Assert.True(touched);
        var stored = Assert.Single(registry.GetSnapshot());
        Assert.Equal(updatedAt, stored.LastUsedUtc);
        Assert.Same(session.Service, stored.Service);
        Assert.True(registry.TryGetProfileId(session.Service, out var profileId));
        Assert.Equal("profile-a", profileId);
    }

    private static AcpConnectionSession CreateSession(string profileId, AcpChatServiceAdapter? service = null)
        => new(
            profileId,
            service ?? CreateAdapter(),
            new InitializeResponse(),
            new AcpConnectionReuseKey(TransportType.Stdio, profileId, string.Empty, string.Empty));

    private static AcpChatServiceAdapter CreateAdapter()
        => new(
            new Mock<IChatService>().Object,
            new AcpEventAdapter(
                _ => { },
                new ImmediateUiDispatcher()));
}
