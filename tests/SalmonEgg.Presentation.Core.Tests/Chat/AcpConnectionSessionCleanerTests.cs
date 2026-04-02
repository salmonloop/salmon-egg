using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
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
        var cleaner = new AcpConnectionSessionCleaner(registry, logger.Object);

        var activeInner = CreateChatService(isConnected: true, isInitialized: true);
        var staleDisconnectedInner = CreateChatService(isConnected: false, isInitialized: true);
        var staleUninitializedInner = CreateChatService(isConnected: true, isInitialized: false);

        var active = WrapAdapter(activeInner.Object);
        var staleDisconnected = WrapAdapter(staleDisconnectedInner.Object);
        var staleUninitialized = WrapAdapter(staleUninitializedInner.Object);

        registry.Upsert(new AcpConnectionSession("active", active, CreateInitializeResponse("active"), "sig-active"));
        registry.Upsert(new AcpConnectionSession("stale-disconnected", staleDisconnected, CreateInitializeResponse("stale-a"), "sig-a"));
        registry.Upsert(new AcpConnectionSession("stale-uninitialized", staleUninitialized, CreateInitializeResponse("stale-b"), "sig-b"));

        var result = await cleaner.CleanupStaleAsync(active, CancellationToken.None);

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
        var cleaner = new AcpConnectionSessionCleaner(registry, logger.Object);

        var staleInner = CreateChatService(isConnected: false, isInitialized: true);
        staleInner
            .Setup(x => x.DisconnectAsync())
            .ThrowsAsync(new InvalidOperationException("disconnect failure"));

        var stale = WrapAdapter(staleInner.Object);
        registry.Upsert(new AcpConnectionSession("stale", stale, CreateInitializeResponse("stale"), "sig-stale"));

        var result = await cleaner.CleanupStaleAsync(activeService: null, CancellationToken.None);

        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(1, result.DisposeFailureCount);
        Assert.False(registry.TryGetByProfile("stale", out _));
        staleInner.Verify(x => x.DisconnectAsync(), Times.Once);
    }

    private static InitializeResponse CreateInitializeResponse(string name)
        => new(1, new AgentInfo(name, "1.0.0"), new AgentCapabilities());

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
