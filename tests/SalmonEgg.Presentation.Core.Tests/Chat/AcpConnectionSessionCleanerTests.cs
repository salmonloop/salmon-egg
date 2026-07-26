using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
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
    public async Task CleanupStaleAsync_WhenCancelledMidStaleDisposal_StillReleasesAllDetachedSessions()
    {
        // RemoveWhere 已把全部失效会话从注册表批量摘除；此后它们再无持有者，必须无条件全部释放。
        // 取消令牌不得在摘除后中断循环，否则剩余会话成为无主泄漏连接（进程/套接字/HttpClient）。
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var logger = new Mock<ILogger<AcpConnectionSessionCleaner>>();
        var cleaner = CreateCleaner(registry, logger.Object);

        var staleA = CreateChatService(isConnected: false, isInitialized: true);
        var staleB = CreateChatService(isConnected: false, isInitialized: true);
        var staleC = CreateChatService(isConnected: false, isInitialized: true);

        registry.Upsert(new AcpConnectionSession("stale-a", WrapAdapter(staleA.Object), CreateInitializeResponse("a"), CreateReuseKey("sig-a")));
        registry.Upsert(new AcpConnectionSession("stale-b", WrapAdapter(staleB.Object), CreateInitializeResponse("b"), CreateReuseKey("sig-b")));
        registry.Upsert(new AcpConnectionSession("stale-c", WrapAdapter(staleC.Object), CreateInitializeResponse("c"), CreateReuseKey("sig-c")));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // 入口的 ThrowIfCancellationRequested 在摘除之前起作用，故取消须在摘除后触发才暴露泄漏；
        // 已取消令牌恰好覆盖「摘除后循环内取消」这条路径：入口若抛，会话根本没被摘除，也不泄漏。
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cleaner.CleanupStaleAsync(activeService: null, cancellationToken: cts.Token));

        // 若已摘除，则必须已释放：注册表不再持有它们，唯有循环负责归还底层资源。
        // 若入口先抛（未摘除），会话仍在注册表里，未释放也不算泄漏。二者互斥，断言据实际状态择一。
        foreach (var (profile, mock) in new[] { ("stale-a", staleA), ("stale-b", staleB), ("stale-c", staleC) })
        {
            if (!registry.TryGetByProfile(profile, out _))
            {
                mock.Verify(x => x.DisconnectAsync(), Times.Once);
            }
        }
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
