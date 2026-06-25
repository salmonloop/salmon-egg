using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public class AcpConnectionSessionCleanerTests
{
    [Fact]
    public async Task CleanupStaleAsync_RemovesStaleSessions()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpConnectionSessionCleaner>>();
        var cleaner = CreateCleaner(registry, logger.Object);

        var staleInner = CreateChatService(isConnected: false, isInitialized: true);
        var stale = WrapAdapter(staleInner.Object);

        registry.Upsert(new AcpConnectionSession("stale", stale, CreateInitializeResponse("stale"), CreateReuseKey("sig-stale")));

        var result = await cleaner.CleanupStaleAsync(
            activeService: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(0, result.DisposeFailureCount);
        Assert.False(registry.TryGetByProfile("stale", out _));
        staleInner.Verify(x => x.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task CleanupStaleAsync_HandlesDisposeFailures()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpConnectionSessionCleaner>>();
        var cleaner = CreateCleaner(registry, logger.Object);

        var staleInner = CreateChatService(isConnected: false, isInitialized: true);
        staleInner.Setup(x => x.DisconnectAsync()).ThrowsAsync(new InvalidOperationException("disconnect error"));
        var stale = WrapAdapter(staleInner.Object);

        registry.Upsert(new AcpConnectionSession("stale", stale, CreateInitializeResponse("stale"), CreateReuseKey("sig-stale")));

        var result = await cleaner.CleanupStaleAsync(
            activeService: null, cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(1, result.DisposeFailureCount);
        Assert.False(registry.TryGetByProfile("stale", out _));
        staleInner.Verify(x => x.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task CleanupStaleAsync_WhenMultipleFailures_CountsCorrectlyWithConcurrency()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpConnectionSessionCleaner>>();
        var cleaner = CreateCleaner(registry, logger.Object);

        var staleInner1 = CreateChatService(isConnected: false, isInitialized: true);
        staleInner1.Setup(x => x.DisconnectAsync()).ThrowsAsync(new InvalidOperationException("disconnect error 1"));
        var stale1 = WrapAdapter(staleInner1.Object);

        var staleInner2 = CreateChatService(isConnected: false, isInitialized: true);
        staleInner2.Setup(x => x.DisconnectAsync()).ThrowsAsync(new InvalidOperationException("disconnect error 2"));
        var stale2 = WrapAdapter(staleInner2.Object);

        registry.Upsert(new AcpConnectionSession("stale1", stale1, CreateInitializeResponse("stale1"), CreateReuseKey("sig-stale1")));
        registry.Upsert(new AcpConnectionSession("stale2", stale2, CreateInitializeResponse("stale2"), CreateReuseKey("sig-stale2")));

        var result = await cleaner.CleanupStaleAsync(
            activeService: null, cancellationToken: CancellationToken.None);

        Assert.Equal(2, result.RemovedCount);
        Assert.Equal(2, result.DisposeFailureCount);
        Assert.False(registry.TryGetByProfile("stale1", out _));
        Assert.False(registry.TryGetByProfile("stale2", out _));
        staleInner1.Verify(x => x.DisconnectAsync(), Times.Once);
        staleInner2.Verify(x => x.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task CleanupStaleAsync_ExcludesActiveService()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpConnectionSessionCleaner>>();
        var cleaner = CreateCleaner(registry, logger.Object, new AcpConnectionEvictionOptions
        {
            EnablePolicyEviction = true,
            MaxWarmProfiles = 0
        });

        // The active service should be kept even if it would otherwise be evicted
        var activeInner = CreateChatService(isConnected: true, isInitialized: true);
        var active = WrapAdapter(activeInner.Object);

        registry.Upsert(new AcpConnectionSession("active", active, CreateInitializeResponse("active"), CreateReuseKey("sig-active"))
        {
            LastUsedUtc = DateTime.UtcNow.AddMinutes(-30)
        });

        var result = await cleaner.CleanupStaleAsync(
            activeService: active, cancellationToken: CancellationToken.None);

        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(0, result.DisposeFailureCount);
        Assert.True(registry.TryGetByProfile("active", out _));
        activeInner.Verify(x => x.DisconnectAsync(), Times.Never);
    }

    [Fact]
    public async Task CleanupStaleAsync_HandlesCancellation()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpConnectionSessionCleaner>>();
        var cleaner = CreateCleaner(registry, logger.Object);

        var staleInner = CreateChatService(isConnected: false, isInitialized: true);
        var stale = WrapAdapter(staleInner.Object);

        registry.Upsert(new AcpConnectionSession("stale", stale, CreateInitializeResponse("stale"), CreateReuseKey("sig-stale")));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await cleaner.CleanupStaleAsync(activeService: null, cancellationToken: cts.Token));

        // Should not have disconnected since it was cancelled before work started
        staleInner.Verify(x => x.DisconnectAsync(), Times.Never);
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

    [Fact]
    public async Task CleanupStaleAsync_WhenPinnedBudgetExceeded_EvictsOldestSoftPinnedButKeepsHardPinned()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpConnectionSessionCleaner>>();
        var cleaner = CreateCleaner(
            registry,
            logger.Object,
            new AcpConnectionEvictionOptions
            {
                EnablePolicyEviction = true,
                MaxPinnedProfiles = 1
            });

        var hardPinned = WrapAdapter(CreateChatService(isConnected: true, isInitialized: true).Object);
        var oldSoftPinned = WrapAdapter(CreateChatService(isConnected: true, isInitialized: true).Object);
        var recentSoftPinned = WrapAdapter(CreateChatService(isConnected: true, isInitialized: true).Object);

        registry.Upsert(new AcpConnectionSession("selected", hardPinned, CreateInitializeResponse("selected"), CreateReuseKey("sig-selected"))
        {
            LastUsedUtc = DateTime.UtcNow.AddMinutes(-12)
        });
        registry.Upsert(new AcpConnectionSession("soft-old", oldSoftPinned, CreateInitializeResponse("soft-old", loadSession: false), CreateReuseKey("sig-soft-old"))
        {
            LastUsedUtc = DateTime.UtcNow.AddMinutes(-10)
        });
        registry.Upsert(new AcpConnectionSession("soft-recent", recentSoftPinned, CreateInitializeResponse("soft-recent", loadSession: false), CreateReuseKey("sig-soft-recent"))
        {
            LastUsedUtc = DateTime.UtcNow.AddMinutes(-2)
        });

        var result = await cleaner.CleanupStaleAsync(
            activeService: null,
            isPinned: session => session.InitializeResponse.AgentCapabilities?.LoadSession != true,
            isHardPinned: session => string.Equals(session.ProfileId, "selected", StringComparison.Ordinal),
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.RemovedCount);
        Assert.True(registry.TryGetByProfile("selected", out _));
        Assert.False(registry.TryGetByProfile("soft-old", out _));
        Assert.True(registry.TryGetByProfile("soft-recent", out _));
    }

    [Fact]
    public async Task CleanupBeforeApplyAsync_NonLoadableBoundProfile_RemainsPinned()
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
        var poolManager = new AcpConnectionPoolManager(
            registry,
            cleaner,
            Mock.Of<ILogger<AcpConnectionPoolManager>>());

        var loadUnsupported = WrapAdapter(CreateChatService(isConnected: true, isInitialized: true).Object);
        var evictable = WrapAdapter(CreateChatService(isConnected: true, isInitialized: true).Object);

        registry.Upsert(new AcpConnectionSession(
            "profile-a",
            loadUnsupported,
            CreateInitializeResponse("agent-a", loadSession: false),
            CreateReuseKey("sig-a"))
        {
            LastUsedUtc = DateTime.UtcNow.AddMinutes(-10)
        });
        registry.Upsert(new AcpConnectionSession(
            "profile-b",
            evictable,
            CreateInitializeResponse("agent-b", loadSession: true),
            CreateReuseKey("sig-b"))
        {
            LastUsedUtc = DateTime.UtcNow.AddMinutes(-8)
        });

        var snapshot = new AcpConnectionDependencySnapshot(
            SelectedProfileId: "profile-z",
            ProfilesRequiredByRemoteBindings: ImmutableHashSet.Create(StringComparer.Ordinal, "profile-a"));

        var result = await poolManager.CleanupBeforeApplyAsync(
            activeService: null,
            snapshot,
            CancellationToken.None);

        Assert.Equal(1, result.RemovedCount);
        Assert.True(registry.TryGetByProfile("profile-a", out _));
        Assert.False(registry.TryGetByProfile("profile-b", out _));
    }

    private static InitializeResponse CreateInitializeResponse(string name, bool loadSession = true)
        => new(1, new AgentInfo(name, "1.0.0"), new AgentCapabilities(loadSession: loadSession));

    private static AcpConnectionSessionCleaner CreateCleaner(
        IAcpConnectionSessionRegistry registry,
        ILogger<AcpConnectionSessionCleaner> logger,
        AcpConnectionEvictionOptions? options = null)
    {
        var configured = options ?? new AcpConnectionEvictionOptions();
        return new AcpConnectionSessionCleaner(
            registry,
            new ConservativeAcpConnectionEvictionPolicy(configured),
            configured,
            logger);
    }

    private static AcpConnectionReuseKey CreateReuseKey(string token)
        => new(TransportType.Stdio, token, token, token);

    private static AcpChatServiceAdapter WrapAdapter(IChatService inner)
        => new(
            inner,
            new AcpEventAdapter(
                _ => { },
                new ImmediateUiDispatcher(),
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
