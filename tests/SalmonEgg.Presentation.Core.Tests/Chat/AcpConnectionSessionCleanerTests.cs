using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class AcpConnectionSessionCleanerTests
{
    [Fact]
    public async Task CleanupStaleAsync_RemovesInvalidSessions_AndKeepsActiveService()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpConnectionSessionCleaner>>();
        var cleaner = CreateCleaner(registry, logger.Object);

        var activeInner = CreateChatService(isConnected: true, isInitialized: true);
        var staleDisconnectedInner = CreateChatService(isConnected: false, isInitialized: true);
        var staleUninitializedInner = CreateChatService(isConnected: true, isInitialized: false);

        var active = WrapAdapter(activeInner.Object);
        var staleDisconnected = WrapAdapter(staleDisconnectedInner.Object);
        var staleUninitialized = WrapAdapter(staleUninitializedInner.Object);

        registry.Upsert(new AcpConnectionSession("active", active, CreateInitializeResponse("active"), CreateReuseKey("sig-active")));
        registry.Upsert(new AcpConnectionSession("stale-disconnected", staleDisconnected, CreateInitializeResponse("stale-a"), CreateReuseKey("sig-a")));
        registry.Upsert(new AcpConnectionSession("stale-uninitialized", staleUninitialized, CreateInitializeResponse("stale-b"), CreateReuseKey("sig-b")));

        var result = await cleaner.CleanupStaleAsync(active, cancellationToken: CancellationToken.None);

        Assert.Equal(2, result.RemovedCount);
        Assert.Equal(0, result.DisposeFailureCount);
        Assert.True(registry.TryGetByProfile("active", out _));
        Assert.False(registry.TryGetByProfile("stale-disconnected", out _));
        Assert.False(registry.TryGetByProfile("stale-uninitialized", out _));
        staleDisconnectedInner.Verify(x => x.DisconnectAsync(), Times.Once);
        staleUninitializedInner.Verify(x => x.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task CleanupStaleAsync_WhenDisconnectThrows_ContinuesAndReportsFailureCount()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpConnectionSessionCleaner>>();
        var cleaner = CreateCleaner(registry, logger.Object);

        var staleInner = CreateChatService(isConnected: false, isInitialized: true);
        staleInner
            .Setup(x => x.DisconnectAsync())
            .ThrowsAsync(new InvalidOperationException("disconnect failure"));

        var stale = WrapAdapter(staleInner.Object);
        registry.Upsert(new AcpConnectionSession("stale", stale, CreateInitializeResponse("stale"), CreateReuseKey("sig-stale")));

        var result = await cleaner.CleanupStaleAsync(activeService: null, cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(1, result.DisposeFailureCount);
        Assert.False(registry.TryGetByProfile("stale", out _));
        staleInner.Verify(x => x.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task CleanupStaleAsync_WhenPolicyEnabled_EvictsOnlyUnpinnedWarmSessions()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpConnectionSessionCleaner>>();
        var cleaner = CreateCleaner(
            registry,
            logger.Object,
            new AcpConnectionEvictionOptions
            {
                EnablePolicyEviction = true,
                MaxWarmProfiles = 1
            });

        var pinnedInner = CreateChatService(isConnected: true, isInitialized: true);
        var oldWarmInner = CreateChatService(isConnected: true, isInitialized: true);
        var recentWarmInner = CreateChatService(isConnected: true, isInitialized: true);

        var pinned = WrapAdapter(pinnedInner.Object);
        var oldWarm = WrapAdapter(oldWarmInner.Object);
        var recentWarm = WrapAdapter(recentWarmInner.Object);

        registry.Upsert(new AcpConnectionSession("pinned", pinned, CreateInitializeResponse("pinned"), CreateReuseKey("sig-p"))
        {
            LastUsedUtc = DateTime.UtcNow.AddMinutes(-30)
        });
        registry.Upsert(new AcpConnectionSession("old", oldWarm, CreateInitializeResponse("old"), CreateReuseKey("sig-old"))
        {
            LastUsedUtc = DateTime.UtcNow.AddMinutes(-20)
        });
        registry.Upsert(new AcpConnectionSession("recent", recentWarm, CreateInitializeResponse("recent"), CreateReuseKey("sig-recent"))
        {
            LastUsedUtc = DateTime.UtcNow.AddMinutes(-1)
        });

        var result = await cleaner.CleanupStaleAsync(
            activeService: null,
            isPinned: session => string.Equals(session.ProfileId, "pinned", StringComparison.Ordinal),
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.RemovedCount);
        Assert.True(registry.TryGetByProfile("pinned", out _));
        Assert.False(registry.TryGetByProfile("old", out _));
        Assert.True(registry.TryGetByProfile("recent", out _));
    }

    [Fact]
    public async Task CleanupStaleAsync_WhenPinnedLoadSessionFalse_DoesNotEvictPinnedProfile()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpConnectionSessionCleaner>>();
        var cleaner = CreateCleaner(
            registry,
            logger.Object,
            new AcpConnectionEvictionOptions
            {
                EnablePolicyEviction = true,
                MaxWarmProfiles = 0
            });

        var loadUnsupported = WrapAdapter(CreateChatService(isConnected: true, isInitialized: true).Object);
        var evictable = WrapAdapter(CreateChatService(isConnected: true, isInitialized: true).Object);

        registry.Upsert(new AcpConnectionSession(
            "load-unsupported",
            loadUnsupported,
            CreateInitializeResponse("agent-a", loadSession: false),
            CreateReuseKey("sig-a"))
        {
            LastUsedUtc = DateTime.UtcNow.AddMinutes(-10)
        });
        registry.Upsert(new AcpConnectionSession(
            "evictable",
            evictable,
            CreateInitializeResponse("agent-b", loadSession: true),
            CreateReuseKey("sig-b"))
        {
            LastUsedUtc = DateTime.UtcNow.AddMinutes(-8)
        });

        var result = await cleaner.CleanupStaleAsync(
            activeService: null,
            isPinned: session => session.InitializeResponse.AgentCapabilities?.LoadSession != true,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.RemovedCount);
        Assert.True(registry.TryGetByProfile("load-unsupported", out _));
        Assert.False(registry.TryGetByProfile("evictable", out _));
    }

    private static InitializeResponse CreateInitializeResponse(string name, bool loadSession = true)
        => new(1, new AgentInfo(name, "1.0.0"), new AgentCapabilities(loadSession: loadSession));

    private static AcpConnectionSessionCleaner CreateCleaner(
        IAcpConnectionSessionRegistry registry,
        ILogger<AcpConnectionSessionCleaner> logger,
        AcpConnectionEvictionOptions? options = null)
        => new(
            registry,
            new ConservativeAcpConnectionEvictionPolicy(options ?? new AcpConnectionEvictionOptions()),
            logger);

    private static AcpConnectionReuseKey CreateReuseKey(string token)
        => new(TransportType.Stdio, token, token, token);

    private static AcpChatServiceAdapter WrapAdapter(IChatService inner)
        => new(
            inner,
            new AcpEventAdapter(
                _ => { },
                new SynchronizationContext(),
                bufferLimit: 16,
                resyncRequired: _ => { }));

    private static Mock<IChatService> CreateChatService(bool isConnected, bool isInitialized)
    {
        var service = new Mock<IChatService>();
        service.SetupGet(x => x.IsConnected).Returns(isConnected);
        service.SetupGet(x => x.IsInitialized).Returns(isInitialized);
        service.Setup(x => x.DisconnectAsync()).ReturnsAsync(true);
        service.SetupGet(x => x.AgentCapabilities).Returns(new AgentCapabilities());
        service.Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent", "1.0.0"), new AgentCapabilities()));
        return service;
    }
}
