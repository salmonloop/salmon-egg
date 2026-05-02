using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class AcpChatCoordinatorTests
{
    [Fact]
    public async Task ConnectToProfileAsync_MapsProfileToTransportAndInitializesService()
    {
        var transport = new FakeTransportConfiguration();
        var sink = new FakeSink();
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null))
            .Returns(service.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Local Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe",
            StdioArgs = "--serve"
        };

        var result = await sut.ConnectToProfileAsync(profile, transport, sink);

        Assert.Equal(TransportType.Stdio, transport.SelectedTransportType);
        Assert.Equal("agent.exe", transport.StdioCommand);
        Assert.Equal("--serve", transport.StdioArgs);
        Assert.Equal(string.Empty, transport.RemoteUrl);
        Assert.IsType<AcpChatServiceAdapter>(sink.CurrentChatService);
        Assert.Equal("profile-1", sink.SelectedProfileId);
        Assert.Equal("agent", sink.AgentName);
        Assert.Equal("1.0.0", sink.AgentVersion);
        Assert.IsType<AcpChatServiceAdapter>(result.ChatService);
        service.Verify(x => x.InitializeAsync(It.Is<InitializeParams>(p =>
            p.ProtocolVersion == 1
            && string.Equals(p.ClientInfo.Name, "SalmonEgg", StringComparison.Ordinal)
            && string.Equals(p.ClientInfo.Title, "SalmonEgg", StringComparison.Ordinal)
            && string.Equals(p.ClientInfo.Version, "1.0.0", StringComparison.Ordinal)
            && p.ClientCapabilities.Terminal == null
            && p.ClientCapabilities.Fs == null
            && p.ClientCapabilities.SupportsExtension(ClientCapabilityMetadata.AskUserExtensionMethod))), Times.Once);
        connectionCoordinator.Verify(
            x => x.SetConnectingAsync("profile-1", It.IsAny<CancellationToken>()),
            Times.Once);
        connectionCoordinator.Verify(
            x => x.SetConnectedAsync("profile-1", It.IsAny<CancellationToken>()),
            Times.Once);
        connectionCoordinator.Verify(
            x => x.ClearAuthenticationRequiredAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConnectToProfileAsync_UsesAuthoritativeDependencySnapshotDuringCleanup()
    {
        var transport = new FakeTransportConfiguration();
        var sink = new FakeSink();
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var snapshotProvider = new Mock<IAcpConnectionDependencySnapshotProvider>(MockBehavior.Strict);
        var poolManager = new RecordingConnectionPoolManager();
        var expectedSnapshot = new AcpConnectionDependencySnapshot(
            "profile-1",
            ImmutableHashSet.Create(StringComparer.Ordinal, "profile-a"));

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null))
            .Returns(service.Object);
        snapshotProvider
            .Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSnapshot);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionPoolManager: poolManager,
            connectionDependencySnapshotProvider: snapshotProvider.Object);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Local Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe",
            StdioArgs = "--serve"
        };

        await sut.ConnectToProfileAsync(profile, transport, sink);

        Assert.Equal(expectedSnapshot, poolManager.LastCleanupSnapshot);
        snapshotProvider.Verify(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectToProfileAsync_WhenSwitchingBackToExistingProfile_ReusesExistingSessionWithoutReinitialize()
    {
        var transport = new FakeTransportConfiguration();
        var sink = new FakeSink();
        var profile1Service = CreateChatService();
        var profile2Service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        profile1Service
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent-p1", "1.0.0"), new AgentCapabilities()));
        profile2Service
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent-p2", "1.0.0"), new AgentCapabilities()));

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent-1.exe", "--serve-1", null))
            .Returns(profile1Service.Object);
        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent-2.exe", "--serve-2", null))
            .Returns(profile2Service.Object);

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        var profile1 = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent 1",
            Transport = TransportType.Stdio,
            StdioCommand = "agent-1.exe",
            StdioArgs = "--serve-1"
        };
        var profile2 = new ServerConfiguration
        {
            Id = "profile-2",
            Name = "Agent 2",
            Transport = TransportType.Stdio,
            StdioCommand = "agent-2.exe",
            StdioArgs = "--serve-2"
        };

        var first = await sut.ConnectToProfileAsync(profile1, transport, sink);
        var second = await sut.ConnectToProfileAsync(profile2, transport, sink);
        var third = await sut.ConnectToProfileAsync(profile1, transport, sink);

        Assert.IsType<AcpChatServiceAdapter>(first.ChatService);
        Assert.IsType<AcpChatServiceAdapter>(second.ChatService);
        Assert.IsType<AcpChatServiceAdapter>(third.ChatService);
        Assert.Same(first.ChatService, third.ChatService);
        Assert.Equal("agent-p1", sink.AgentName);

        profile1Service.Verify(x => x.InitializeAsync(It.IsAny<InitializeParams>()), Times.Once);
        profile2Service.Verify(x => x.InitializeAsync(It.IsAny<InitializeParams>()), Times.Once);
        profile1Service.Verify(x => x.DisconnectAsync(), Times.Never);
        profile2Service.Verify(x => x.DisconnectAsync(), Times.Never);
        factory.Verify(x => x.CreateChatService(TransportType.Stdio, "agent-1.exe", "--serve-1", null), Times.Once);
        factory.Verify(x => x.CreateChatService(TransportType.Stdio, "agent-2.exe", "--serve-2", null), Times.Once);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_WhenAgentTitleExists_UsesTitleForDisplayedIdentity()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var chatService = CreateChatService();
        var sink = new FakeSink();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        chatService
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(
                1,
                new AgentInfo("@zed-industries/claude-agent-acp", "0.20.2", "Claude Agent"),
                new AgentCapabilities()));

        factory
            .Setup(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(chatService.Object);

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        await sut.ApplyTransportConfigurationAsync(transport, sink, preserveConversation: false);

        Assert.Equal("Claude Agent", sink.AgentName);
        Assert.Equal("0.20.2", sink.AgentVersion);
    }

    [Fact]
    public async Task ConnectToProfileAsync_WhenReusingSameActiveProfile_DoesNotDisconnectCurrentService()
    {
        var transport = new FakeTransportConfiguration();
        var sink = new FakeSink();
        var profileService = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionInstanceIds = new List<string?>();
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var cleaner = new AcpConnectionSessionCleaner(
            registry,
            new ConservativeAcpConnectionEvictionPolicy(new AcpConnectionEvictionOptions()),
            new AcpConnectionEvictionOptions(),
            Mock.Of<ILogger<AcpConnectionSessionCleaner>>());
        var poolManager = new AcpConnectionPoolManager(
            registry,
            cleaner,
            Mock.Of<ILogger<AcpConnectionPoolManager>>());
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();

        profileService
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent-p1", "1.0.0"), new AgentCapabilities()));
        connectionCoordinator
            .Setup(x => x.SetConnectionInstanceIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string?, CancellationToken>((id, _) => connectionInstanceIds.Add(id))
            .Returns(Task.CompletedTask);

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent-1.exe", "--serve-1", null))
            .Returns(profileService.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object,
            sessionRegistry: registry,
            sessionCleaner: cleaner,
            connectionPoolManager: poolManager);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent 1",
            Transport = TransportType.Stdio,
            StdioCommand = "agent-1.exe",
            StdioArgs = "--serve-1"
        };

        var first = await sut.ConnectToProfileAsync(profile, transport, sink);
        var second = await sut.ConnectToProfileAsync(profile, transport, sink);

        Assert.Same(first.ChatService, second.ChatService);
        profileService.Verify(x => x.InitializeAsync(It.IsAny<InitializeParams>()), Times.Once);
        profileService.Verify(x => x.DisconnectAsync(), Times.Never);
        factory.Verify(x => x.CreateChatService(TransportType.Stdio, "agent-1.exe", "--serve-1", null), Times.Once);
        Assert.Equal(2, connectionInstanceIds.Count);
        Assert.False(string.IsNullOrWhiteSpace(connectionInstanceIds[0]));
        Assert.Equal(connectionInstanceIds[0], connectionInstanceIds[1]);
        Assert.True(registry.TryGetByProfile("profile-1", out var cachedSession));
        Assert.Equal(connectionInstanceIds[1], cachedSession!.ConnectionInstanceId);
    }

    [Fact]
    public async Task ConnectToProfileAsync_WhenConversationIsPreserved_UsesPoolOnlyServiceReplaceIntent()
    {
        var transport = new FakeTransportConfiguration();
        var sink = new FakeSink();
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null))
            .Returns(service.Object);

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe",
            StdioArgs = "--serve"
        };

        await sut.ConnectToProfileAsync(
            profile,
            transport,
            sink,
            new AcpConnectionContext("conv-1", PreserveConversation: true, ActivationVersion: 1));

        Assert.Contains(ServiceReplaceIntent.PoolOnly, sink.ReplaceChatServiceIntents);
        Assert.DoesNotContain(ServiceReplaceIntent.ForegroundOwner, sink.ReplaceChatServiceIntents);
    }

    [Fact]
    public async Task ConnectToProfileAsync_WhenProfileConfigChangesOnlyByWhitespaceAndArgSpacing_ReusesSession()
    {
        var transport = new FakeTransportConfiguration();
        var sink = new FakeSink();
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        service
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent", "1.0.0"), new AgentCapabilities()));

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "  agent.exe  ", " --serve   --mode   plan ", null))
            .Returns(service.Object);

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        var profileWithSpacing = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "  agent.exe  ",
            StdioArgs = " --serve   --mode   plan "
        };
        var normalizedProfile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe",
            StdioArgs = "--serve --mode plan"
        };

        var first = await sut.ConnectToProfileAsync(profileWithSpacing, transport, sink);
        var second = await sut.ConnectToProfileAsync(normalizedProfile, transport, sink);

        Assert.Same(first.ChatService, second.ChatService);
        service.Verify(x => x.InitializeAsync(It.IsAny<InitializeParams>()), Times.Once);
        service.Verify(x => x.DisconnectAsync(), Times.Never);
        factory.Verify(x => x.CreateChatService(TransportType.Stdio, "  agent.exe  ", " --serve   --mode   plan ", null), Times.Once);
        factory.Verify(x => x.CreateChatService(TransportType.Stdio, "agent.exe", "--serve --mode plan", null), Times.Never);
    }

    [Fact]
    public async Task ConnectToProfileAsync_WhenProfileConfigChangesWithSameId_RecreatesSession()
    {
        var transport = new FakeTransportConfiguration();
        var sink = new FakeSink();
        var firstService = CreateChatService();
        var secondService = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionInstanceIds = new List<string?>();
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var cleaner = new AcpConnectionSessionCleaner(
            registry,
            new ConservativeAcpConnectionEvictionPolicy(new AcpConnectionEvictionOptions()),
            new AcpConnectionEvictionOptions(),
            Mock.Of<ILogger<AcpConnectionSessionCleaner>>());
        var poolManager = new AcpConnectionPoolManager(
            registry,
            cleaner,
            Mock.Of<ILogger<AcpConnectionPoolManager>>());
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();

        firstService
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent-old", "1.0.0"), new AgentCapabilities()));
        secondService
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent-new", "1.0.0"), new AgentCapabilities()));
        connectionCoordinator
            .Setup(x => x.SetConnectionInstanceIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string?, CancellationToken>((id, _) => connectionInstanceIds.Add(id))
            .Returns(Task.CompletedTask);

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent-old.exe", "--serve-old", null))
            .Returns(firstService.Object);
        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent-new.exe", "--serve-new", null))
            .Returns(secondService.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object,
            sessionRegistry: registry,
            sessionCleaner: cleaner,
            connectionPoolManager: poolManager);

        var oldProfile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent Old",
            Transport = TransportType.Stdio,
            StdioCommand = "agent-old.exe",
            StdioArgs = "--serve-old"
        };
        var newProfile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent New",
            Transport = TransportType.Stdio,
            StdioCommand = "agent-new.exe",
            StdioArgs = "--serve-new"
        };

        var first = await sut.ConnectToProfileAsync(oldProfile, transport, sink);
        var second = await sut.ConnectToProfileAsync(newProfile, transport, sink);

        Assert.NotSame(first.ChatService, second.ChatService);
        Assert.Equal("agent-new", sink.AgentName);
        firstService.Verify(x => x.InitializeAsync(It.IsAny<InitializeParams>()), Times.Once);
        secondService.Verify(x => x.InitializeAsync(It.IsAny<InitializeParams>()), Times.Once);
        firstService.Verify(x => x.DisconnectAsync(), Times.Once);
        Assert.Equal(2, connectionInstanceIds.Count);
        Assert.NotEqual(connectionInstanceIds[0], connectionInstanceIds[1]);
        Assert.True(registry.TryGetByProfile("profile-1", out var cachedSession));
        Assert.Equal(connectionInstanceIds[1], cachedSession!.ConnectionInstanceId);
    }

    [Fact]
    public async Task ConnectToProfileAsync_WhenCachedSessionBecomesInvalid_RecreatesAndReusesFreshSession()
    {
        var transport = new FakeTransportConfiguration();
        var sink = new FakeSink();
        var staleService = CreateChatService();
        var freshService = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var staleConnected = true;

        staleService.SetupGet(x => x.IsConnected).Returns(() => staleConnected);
        staleService
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent-stale", "1.0.0"), new AgentCapabilities()));
        freshService
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent-fresh", "1.0.0"), new AgentCapabilities()));

        factory
            .SetupSequence(x => x.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null))
            .Returns(staleService.Object)
            .Returns(freshService.Object);

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe",
            StdioArgs = "--serve"
        };

        var first = await sut.ConnectToProfileAsync(profile, transport, sink);
        staleConnected = false;

        var second = await sut.ConnectToProfileAsync(profile, transport, sink);
        var third = await sut.ConnectToProfileAsync(profile, transport, sink);

        Assert.NotSame(first.ChatService, second.ChatService);
        Assert.Same(second.ChatService, third.ChatService);
        Assert.Equal("agent-fresh", sink.AgentName);
        staleService.Verify(x => x.DisconnectAsync(), Times.Once);
        factory.Verify(x => x.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null), Times.Exactly(2));
    }

    [Fact]
    public async Task ConnectToProfileAsync_WhenReusingCachedSession_RefreshesLastUsedTimestamp()
    {
        var transport = new FakeTransportConfiguration();
        var sink = new FakeSink();
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var cleaner = new AcpConnectionSessionCleaner(
            registry,
            new ConservativeAcpConnectionEvictionPolicy(new AcpConnectionEvictionOptions()),
            new AcpConnectionEvictionOptions(),
            new Mock<ILogger<AcpConnectionSessionCleaner>>().Object);

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null))
            .Returns(service.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            sessionRegistry: registry,
            sessionCleaner: cleaner);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent 1",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe",
            StdioArgs = "--serve"
        };

        await sut.ConnectToProfileAsync(profile, transport, sink);
        Assert.True(registry.TryGetByProfile("profile-1", out var cached));

        var oldTimestamp = DateTime.UtcNow.AddMinutes(-10);
        registry.Upsert(cached with { LastUsedUtc = oldTimestamp });

        await Task.Delay(20);
        await sut.ConnectToProfileAsync(profile, transport, sink);

        Assert.True(registry.TryGetByProfile("profile-1", out var refreshed));
        Assert.True(
            refreshed.LastUsedUtc > oldTimestamp,
            $"Expected cached session LastUsedUtc to be refreshed. old={oldTimestamp:O}, current={refreshed.LastUsedUtc:O}");
        service.Verify(x => x.InitializeAsync(It.IsAny<InitializeParams>()), Times.Once);
        factory.Verify(x => x.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null), Times.Once);
    }

    [Fact]
    public async Task ConnectToProfileAsync_WhenPruningStaleBackgroundSessionFails_DoesNotBlockActiveProfileReuse()
    {
        var transport = new FakeTransportConfiguration();
        var sink = new FakeSink();
        var staleBackgroundService = CreateChatService();
        var activeService = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var staleConnected = true;

        staleBackgroundService.SetupGet(x => x.IsConnected).Returns(() => staleConnected);
        staleBackgroundService
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent-p1", "1.0.0"), new AgentCapabilities()));
        activeService
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent-p2", "1.0.0"), new AgentCapabilities()));

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent-1.exe", "--serve-1", null))
            .Returns(staleBackgroundService.Object);
        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent-2.exe", "--serve-2", null))
            .Returns(activeService.Object);

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        var profile1 = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent 1",
            Transport = TransportType.Stdio,
            StdioCommand = "agent-1.exe",
            StdioArgs = "--serve-1"
        };
        var profile2 = new ServerConfiguration
        {
            Id = "profile-2",
            Name = "Agent 2",
            Transport = TransportType.Stdio,
            StdioCommand = "agent-2.exe",
            StdioArgs = "--serve-2"
        };

        await sut.ConnectToProfileAsync(profile1, transport, sink);
        var second = await sut.ConnectToProfileAsync(profile2, transport, sink);
        staleConnected = false;
        staleBackgroundService
            .Setup(x => x.DisconnectAsync())
            .ThrowsAsync(new InvalidOperationException("stale cleanup failure"));

        var third = await sut.ConnectToProfileAsync(profile2, transport, sink);

        Assert.Same(second.ChatService, third.ChatService);
        Assert.Equal("agent-p2", sink.AgentName);
        staleBackgroundService.Verify(x => x.DisconnectAsync(), Times.Once);
        factory.Verify(x => x.CreateChatService(TransportType.Stdio, "agent-1.exe", "--serve-1", null), Times.Once);
        factory.Verify(x => x.CreateChatService(TransportType.Stdio, "agent-2.exe", "--serve-2", null), Times.Once);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_DisconnectsExistingServiceBeforeReplacingIt()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var oldService = CreateChatService();
        var newService = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = oldService.Object
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        factory
            .Setup(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(newService.Object);

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        var result = await sut.ApplyTransportConfigurationAsync(transport, sink, preserveConversation: true);

        oldService.Verify(x => x.DisconnectAsync(), Times.Once);
        Assert.IsType<AcpChatServiceAdapter>(sink.CurrentChatService);
        Assert.IsType<AcpChatServiceAdapter>(result.ChatService);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_CancelledBeforeCandidateCommit_DiscardsCandidateAndKeepsExistingService()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var previousService = CreateChatService();
        var candidateService = CreateChatService();
        var initializeStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowInitializeCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousDisposed = false;
        var candidateDisposed = false;
        previousService.As<IDisposable>()
            .Setup(x => x.Dispose())
            .Callback(() => previousDisposed = true);
        candidateService.As<IDisposable>()
            .Setup(x => x.Dispose())
            .Callback(() => candidateDisposed = true);
        var sink = new FakeSink
        {
            CurrentChatService = previousService.Object,
            IsConnected = true,
            IsInitialized = true,
            CurrentSessionId = "local-session-1",
            SelectedProfileId = "profile-1",
            ConnectionInstanceId = "conn-prev"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();
        var sequence = new MockSequence();

        connectionCoordinator.InSequence(sequence)
            .Setup(x => x.SetConnectingAsync("profile-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        connectionCoordinator.InSequence(sequence)
            .Setup(x => x.SetInitializingAsync("profile-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        connectionCoordinator.InSequence(sequence)
            .Setup(x => x.SetConnectionInstanceIdAsync("conn-prev", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        connectionCoordinator.InSequence(sequence)
            .Setup(x => x.SetConnectedAsync("profile-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        candidateService
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .Returns(async () =>
            {
                initializeStarted.TrySetResult(null);
                await allowInitializeCompletion.Task;
                return new InitializeResponse(1, new AgentInfo("agent", "1.0.0"), new AgentCapabilities());
            });

        factory
            .Setup(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(candidateService.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object);
        using var cancellation = new CancellationTokenSource();

        var applyTask = sut.ApplyTransportConfigurationAsync(
            transport,
            sink,
            new AcpConnectionContext("local-session-1", PreserveConversation: false),
            cancellation.Token);
        await initializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        allowInitializeCompletion.TrySetResult(null);

        await Assert.ThrowsAsync<OperationCanceledException>(() => applyTask);

        Assert.Same(previousService.Object, sink.CurrentChatService);
        Assert.Empty(sink.ReplaceChatServiceCalls);
        previousService.Verify(x => x.DisconnectAsync(), Times.Never);
        candidateService.Verify(x => x.DisconnectAsync(), Times.Once);
        Assert.False(previousDisposed);
        Assert.True(candidateDisposed);
        connectionCoordinator.Verify(
            x => x.SetConnectionInstanceIdAsync("conn-prev", It.IsAny<CancellationToken>()),
            Times.Once);
        connectionCoordinator.Verify(
            x => x.SetDisconnectedAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_CancelledBeforeCandidateCommit_WithoutPreviousConnection_RestoresDisconnectedState()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var candidateService = CreateChatService();
        var initializeStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowInitializeCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var candidateDisposed = false;
        candidateService.As<IDisposable>()
            .Setup(x => x.Dispose())
            .Callback(() => candidateDisposed = true);
        var sink = new FakeSink
        {
            CurrentSessionId = "local-session-1",
            SelectedProfileId = "profile-2"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();

        candidateService
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .Returns(async () =>
            {
                initializeStarted.TrySetResult(null);
                await allowInitializeCompletion.Task;
                return new InitializeResponse(1, new AgentInfo("agent", "1.0.0"), new AgentCapabilities());
            });

        factory
            .Setup(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(candidateService.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object);
        using var cancellation = new CancellationTokenSource();

        var applyTask = sut.ApplyTransportConfigurationAsync(
            transport,
            sink,
            new AcpConnectionContext("local-session-1", PreserveConversation: false),
            cancellation.Token);
        await initializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        allowInitializeCompletion.TrySetResult(null);

        await Assert.ThrowsAsync<OperationCanceledException>(() => applyTask);

        Assert.Null(sink.CurrentChatService);
        Assert.Empty(sink.ReplaceChatServiceCalls);
        candidateService.Verify(x => x.DisconnectAsync(), Times.Once);
        Assert.True(candidateDisposed);
        connectionCoordinator.Verify(
            x => x.SetConnectingAsync("profile-2", It.IsAny<CancellationToken>()),
            Times.Once);
        connectionCoordinator.Verify(
            x => x.SetInitializingAsync("profile-2", It.IsAny<CancellationToken>()),
            Times.Once);
        connectionCoordinator.Verify(
            x => x.SetDisconnectedAsync(null, It.IsAny<CancellationToken>()),
            Times.Once);
        connectionCoordinator.Verify(
            x => x.SetConnectedAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_WhenSecondApplyStarts_FirstApplyMustNotOverrideCommittedService()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var firstCandidate = CreateChatService();
        var secondCandidate = CreateChatService();
        var firstInitializeStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstInitializeCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new FakeSink
        {
            CurrentSessionId = "local-session-1",
            SelectedProfileId = "profile-1"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();

        firstCandidate
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .Returns(async () =>
            {
                firstInitializeStarted.TrySetResult(null);
                await allowFirstInitializeCompletion.Task;
                return new InitializeResponse(1, new AgentInfo("first", "1.0.0"), new AgentCapabilities());
            });

        secondCandidate
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("second", "1.0.0"), new AgentCapabilities()));

        factory
            .SetupSequence(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(firstCandidate.Object)
            .Returns(secondCandidate.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object);

        var firstApplyTask = sut.ApplyTransportConfigurationAsync(
            transport,
            sink,
            new AcpConnectionContext("local-session-1", PreserveConversation: false),
            CancellationToken.None);
        await firstInitializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var secondApplyResult = await sut.ApplyTransportConfigurationAsync(
            transport,
            sink,
            new AcpConnectionContext("local-session-1", PreserveConversation: false),
            CancellationToken.None);

        allowFirstInitializeCompletion.TrySetResult(null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstApplyTask);
        Assert.Same(secondApplyResult.ChatService, sink.CurrentChatService);
        Assert.Equal("second", sink.AgentName);
        firstCandidate.Verify(x => x.DisconnectAsync(), Times.Once);
        secondCandidate.Verify(x => x.DisconnectAsync(), Times.Never);
        connectionCoordinator.Verify(
            x => x.SetDisconnectedAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_WhenSupersededWithoutPreviousService_DoesNotRollbackToDisconnected()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var firstCandidate = CreateChatService();
        var secondCandidate = CreateChatService();
        var firstInitializeStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstInitializeCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new FakeSink
        {
            CurrentSessionId = "local-session-1",
            SelectedProfileId = "profile-1"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();

        firstCandidate
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .Returns(async () =>
            {
                firstInitializeStarted.TrySetResult(null);
                await allowFirstInitializeCompletion.Task;
                return new InitializeResponse(1, new AgentInfo("first", "1.0.0"), new AgentCapabilities());
            });

        secondCandidate
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("second", "1.0.0"), new AgentCapabilities()));

        factory
            .SetupSequence(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(firstCandidate.Object)
            .Returns(secondCandidate.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object);

        var firstApplyTask = sut.ApplyTransportConfigurationAsync(
            transport,
            sink,
            new AcpConnectionContext("local-session-1", PreserveConversation: false),
            CancellationToken.None);
        await firstInitializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await sut.ApplyTransportConfigurationAsync(
            transport,
            sink,
            new AcpConnectionContext("local-session-1", PreserveConversation: false),
            CancellationToken.None);

        allowFirstInitializeCompletion.TrySetResult(null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstApplyTask);

        Assert.NotNull(sink.CurrentChatService);
        Assert.Equal("second", sink.AgentName);
        connectionCoordinator.Verify(
            x => x.SetDisconnectedAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_WhenSupersededApplyFaults_DoesNotWriteDisconnectedState()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var firstCandidate = CreateChatService();
        var secondCandidate = CreateChatService();
        var firstInitializeStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstFault = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new FakeSink
        {
            CurrentSessionId = "local-session-1",
            SelectedProfileId = "profile-1"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();

        firstCandidate
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .Returns(async () =>
            {
                firstInitializeStarted.TrySetResult(null);
                await allowFirstFault.Task;
                throw new InvalidOperationException("first apply fault");
            });

        secondCandidate
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("second", "1.0.0"), new AgentCapabilities()));

        factory
            .SetupSequence(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(firstCandidate.Object)
            .Returns(secondCandidate.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object);

        var firstApplyTask = sut.ApplyTransportConfigurationAsync(
            transport,
            sink,
            new AcpConnectionContext("local-session-1", PreserveConversation: false),
            CancellationToken.None);
        await firstInitializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await sut.ApplyTransportConfigurationAsync(
            transport,
            sink,
            new AcpConnectionContext("local-session-1", PreserveConversation: false),
            CancellationToken.None);

        allowFirstFault.TrySetResult(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstApplyTask);

        Assert.NotNull(sink.CurrentChatService);
        Assert.Equal("second", sink.AgentName);
        connectionCoordinator.Verify(
            x => x.SetDisconnectedAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_PreserveConversationWithExistingBinding_DoesNotHydrateDuringTransportApply()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var service = CreateChatService(new AgentCapabilities(loadSession: true));
        var sink = new FakeSink
        {
            IsSessionActive = true,
            CurrentSessionId = "local-session-1",
            CurrentRemoteSessionId = "remote-session-1"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        factory
            .Setup(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(service.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: new AcpConnectionCoordinator(
                Mock.Of<IChatConnectionStore>(),
                Mock.Of<ILogger<AcpConnectionCoordinator>>()));

        await sut.ApplyTransportConfigurationAsync(transport, sink, preserveConversation: true);

        service.Verify(x => x.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, sink.ResetHydratedConversationForResyncCalls);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_PreserveConversationWithExistingBinding_SkipsResyncWhenLoadSessionCapabilityIsNotAdvertised()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var service = CreateChatService(new AgentCapabilities(loadSession: false));
        var sink = new FakeSink
        {
            IsSessionActive = true,
            CurrentSessionId = "local-session-1",
            CurrentRemoteSessionId = "remote-session-1"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        factory
            .Setup(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(service.Object);

        service
            .Setup(x => x.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionLoadResponse.Completed);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: new AcpConnectionCoordinator(
                Mock.Of<IChatConnectionStore>(),
                Mock.Of<ILogger<AcpConnectionCoordinator>>()));

        await sut.ApplyTransportConfigurationAsync(transport, sink, preserveConversation: true);

        service.Verify(x => x.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, sink.ResetHydratedConversationForResyncCalls);
    }

    [Fact]
    public async Task EnsureRemoteSessionAsync_CreatesAndBindsRemoteSessionWhenMissing()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            IsConnected = true,
            IsInitialized = true,
            IsSessionActive = true,
            CurrentSessionId = "local-session-1",
            ActiveSessionCwd = @"C:\repo\demo",
            SelectedProfileId = "profile-1"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        service
            .Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-session-1"));

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        var result = await sut.EnsureRemoteSessionAsync(sink, _ => Task.FromResult(true));

        Assert.Equal("remote-session-1", result.RemoteSessionId);
        Assert.Equal("remote-session-1", sink.CurrentRemoteSessionId);
        Assert.Single(sink.BindingCommands.Updates);
        Assert.Equal("local-session-1", sink.BindingCommands.Updates[0].ConversationId);
        Assert.Equal("profile-1", sink.BindingCommands.Updates[0].ProfileId);
    }

    [Fact]
    public async Task EnsureRemoteSessionAsync_WhenSelectedProfileChangesWhilePending_UsesOriginalProfileBinding()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            IsConnected = true,
            IsInitialized = true,
            IsSessionActive = true,
            CurrentSessionId = "local-session-1",
            ActiveSessionCwd = @"C:\repo\demo",
            SelectedProfileId = "profile-1"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var createSessionTcs = new TaskCompletionSource<SessionNewResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        service
            .Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .Returns(createSessionTcs.Task);

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);
        var ensureTask = sut.EnsureRemoteSessionAsync(sink, _ => Task.FromResult(true));

        sink.SelectedProfileId = "profile-2";
        createSessionTcs.SetResult(new SessionNewResponse("remote-session-1"));

        var result = await ensureTask;

        Assert.Equal("remote-session-1", result.RemoteSessionId);
        Assert.Single(sink.BindingCommands.Updates);
        Assert.Equal("profile-1", sink.BindingCommands.Updates[0].ProfileId);
    }

    [Fact]
    public async Task DispatchPromptToRemoteSessionAsync_UsesProvidedRemoteSessionId()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            IsConnected = true,
            IsInitialized = true
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        service
            .Setup(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionPromptResponse());

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        await sut.DispatchPromptToRemoteSessionAsync("remote-123", "hi", promptMessageId: null, sink, _ => Task.FromResult(true));

        service.Verify(x => x.SendPromptAsync(It.Is<SessionPromptParams>(p => p.SessionId == "remote-123"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchPromptToRemoteSessionAsync_ForwardsPromptMessageId_ToSessionPrompt()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            IsConnected = true,
            IsInitialized = true
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        SessionPromptParams? captured = null;

        service
            .Setup(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Callback<SessionPromptParams, CancellationToken>((parameters, _) => captured = parameters)
            .ReturnsAsync(new SessionPromptResponse(StopReason.EndTurn, "user-message-1"));

        IAcpConnectionCommands sut = new AcpChatCoordinator(factory.Object, logger.Object);

        var result = await sut.DispatchPromptToRemoteSessionAsync(
            "remote-123",
            "hi",
            "client-msg-1",
            sink,
            _ => Task.FromResult(true));

        Assert.NotNull(captured);
        Assert.Equal("remote-123", captured!.SessionId);
        Assert.Equal("client-msg-1", captured.MessageId);
        Assert.Equal("user-message-1", result.Response.UserMessageId);
    }

    [Fact]
    public async Task DispatchPromptToRemoteSessionAsync_PreservesUserMessageId_FromPromptResponse()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            IsConnected = true,
            IsInitialized = true
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        service
            .Setup(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionPromptResponse(StopReason.EndTurn, "user-message-42"));

        IAcpConnectionCommands sut = new AcpChatCoordinator(factory.Object, logger.Object);

        var result = await sut.DispatchPromptToRemoteSessionAsync(
            "remote-123",
            "hi",
            "client-msg-42",
            sink,
            _ => Task.FromResult(true));

        Assert.Equal("user-message-42", result.Response.UserMessageId);
    }

    [Theory]
    [InlineData(StopReason.Refusal)]
    [InlineData(StopReason.Cancelled)]
    public async Task DispatchPromptToRemoteSessionAsync_PreservesPromptStopReason(StopReason expected)
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            IsConnected = true,
            IsInitialized = true
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        service
            .Setup(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionPromptResponse(expected));

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        var result = await sut.DispatchPromptToRemoteSessionAsync("remote-123", "hi", promptMessageId: null, sink, _ => Task.FromResult(true));

        Assert.Equal(expected, result.Response.StopReason);
    }

    [Fact]
    public async Task DispatchPromptToRemoteSessionAsync_RemoteSessionNotFound_RecreatesSessionAndRetriesOnce()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            IsConnected = true,
            IsInitialized = true,
            IsSessionActive = true,
            CurrentSessionId = "local-1",
            ActiveSessionCwd = @"C:\repo\demo",
            SelectedProfileId = "profile-1"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var first = true;
        var promptMessageId = "client-msg-retry";
        var sentPrompts = new List<SessionPromptParams>();

        service
            .Setup(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionPromptParams, CancellationToken>((p, _) => {
                sentPrompts.Add(p);
                if (first) {
                    first = false;
                    throw new AcpException(JsonRpcErrorCode.ResourceNotFound, "Not found");
                }
                return Task.FromResult(new SessionPromptResponse());
            });

        service
            .Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-new"));

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);
        IAcpConnectionCommands commands = sut;

        var result = await commands.DispatchPromptToRemoteSessionAsync("remote-stale", "hi", promptMessageId, sink, _ => Task.FromResult(true));

        Assert.True(result.RetriedAfterSessionRecovery);
        Assert.Equal("remote-new", result.RemoteSessionId);
        Assert.Equal(2, sentPrompts.Count);
        Assert.Equal("client-msg-retry", sentPrompts[0].MessageId);
        Assert.Equal("client-msg-retry", sentPrompts[1].MessageId);
        service.Verify(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()), Times.Once);
        service.Verify(x => x.SendPromptAsync(It.Is<SessionPromptParams>(p => p.SessionId == "remote-new"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendPromptAsync_DelegatesToEnsureAndDispatchFlow()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            IsConnected = true,
            IsInitialized = true,
            IsSessionActive = true,
            CurrentSessionId = "local-1",
            ActiveSessionCwd = @"C:\repo\demo",
            SelectedProfileId = "profile-1"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        service
            .Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-1"));

        service
            .Setup(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionPromptResponse());

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        var result = await sut.SendPromptAsync("hi", promptMessageId: null, sink, _ => Task.FromResult(true));

        Assert.Equal("remote-1", result.RemoteSessionId);
        service.Verify(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()), Times.Once);
        service.Verify(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelPromptAsync_UsesCurrentRemoteSessionFromState()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            CurrentSessionId = "local-session-1",
            CurrentRemoteSessionId = "remote-session-9"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        SessionCancelParams? captured = null;

        service
            .Setup(x => x.CancelSessionAsync(It.IsAny<SessionCancelParams>()))
            .Callback<SessionCancelParams>(parameters => captured = parameters)
            .ReturnsAsync(new SessionCancelResponse(true));

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        await sut.CancelPromptAsync(sink, "User cancelled");

        Assert.NotNull(captured);
        Assert.Equal("remote-session-9", captured!.SessionId);
        Assert.Equal("User cancelled", captured.Reason);
    }

    [Fact]
    public async Task SendPromptAsync_UsesAuthoritativeResolvedBinding_WhenProjectedRemoteSessionIdIsStale()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            IsConnected = true,
            IsInitialized = true,
            IsSessionActive = true,
            CurrentSessionId = "local-session-1",
            CurrentRemoteSessionId = "remote-stale",
            ResolvedBinding = new ConversationRemoteBindingState("local-session-1", "remote-fresh", "profile-1")
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        service
            .Setup(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionPromptParams, CancellationToken>((parameters, _) =>
            {
                Assert.Equal("remote-fresh", parameters.SessionId);
                return Task.FromResult(new SessionPromptResponse());
            });

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        var result = await sut.SendPromptAsync("hello", promptMessageId: null, sink, _ => Task.FromResult(true));

        Assert.Equal("remote-fresh", result.RemoteSessionId);
        service.Verify(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()), Times.Never);
    }

    [Fact]
    public async Task CancelPromptAsync_UsesAuthoritativeResolvedBinding_WhenProjectedRemoteSessionIdIsStale()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            CurrentRemoteSessionId = "remote-stale",
            ResolvedBinding = new ConversationRemoteBindingState("local-session-1", "remote-fresh", "profile-1")
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        SessionCancelParams? captured = null;

        service
            .Setup(x => x.CancelSessionAsync(It.IsAny<SessionCancelParams>()))
            .Callback<SessionCancelParams>(parameters => captured = parameters)
            .ReturnsAsync(new SessionCancelResponse(true));

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        await sut.CancelPromptAsync(sink, "User cancelled");

        Assert.NotNull(captured);
        Assert.Equal("remote-fresh", captured!.SessionId);
    }

    [Fact]
    public async Task DisconnectAsync_DisconnectsServiceAndClearsCoordinatorState()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
            IsConnected = true,
            IsInitialized = true,
            CurrentSessionId = "local-session-1",
            CurrentRemoteSessionId = "remote-session-3"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();
        var sequence = new MockSequence();
        connectionCoordinator.InSequence(sequence)
            .Setup(x => x.SetConnectionInstanceIdAsync(null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        connectionCoordinator.InSequence(sequence)
            .Setup(x => x.ResetAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object);

        await sut.DisconnectAsync(sink);

        service.Verify(x => x.DisconnectAsync(), Times.Once);
        Assert.Null(sink.CurrentChatService);
        Assert.Null(sink.CurrentRemoteSessionId);
        Assert.Equal(1, sink.BindingCommands.ClearCalls);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_BufferOverflow_DelegatesResyncToConnectionCoordinator()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var service = CreateChatService();
        var dispatcher = new QueueingUiDispatcher();
        var sink = new FakeSink
        {
            IsSessionActive = true,
            CurrentSessionId = "local-session-1",
            CurrentRemoteSessionId = "remote-session-1",
            Dispatcher = dispatcher
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();

        factory
            .Setup(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(service.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object,
            sessionUpdateBufferLimit: 1);

        await sut.ApplyTransportConfigurationAsync(transport, sink, preserveConversation: true);

        service.Raise(
            x => x.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-session-1", new PlanUpdate(title: "one")));
        service.Raise(
            x => x.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-session-1", new PlanUpdate(title: "two")));
        dispatcher.RunAll();

        connectionCoordinator.Verify(
            x => x.ResyncAsync(sink, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_WhenStaleSupersededServiceOverflowsBuffer_DoesNotTriggerResync()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var firstCandidate = CreateChatService();
        var secondCandidate = CreateChatService();
        var firstInitializeStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstInitializeCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new QueueingUiDispatcher();
        var sink = new FakeSink
        {
            IsSessionActive = true,
            CurrentSessionId = "local-session-1",
            CurrentRemoteSessionId = "remote-session-1",
            Dispatcher = dispatcher,
            SelectedProfileId = "profile-1"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();

        firstCandidate
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .Returns(async () =>
            {
                firstInitializeStarted.TrySetResult(null);
                await allowFirstInitializeCompletion.Task;
                return new InitializeResponse(1, new AgentInfo("first", "1.0.0"), new AgentCapabilities(loadSession: true));
            });

        secondCandidate
            .Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("second", "1.0.0"), new AgentCapabilities(loadSession: true)));

        factory
            .SetupSequence(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(firstCandidate.Object)
            .Returns(secondCandidate.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object,
            sessionUpdateBufferLimit: 1);

        var firstApplyTask = sut.ApplyTransportConfigurationAsync(
            transport,
            sink,
            new AcpConnectionContext("local-session-1", PreserveConversation: true),
            CancellationToken.None);
        await firstInitializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await sut.ApplyTransportConfigurationAsync(
            transport,
            sink,
            new AcpConnectionContext("local-session-1", PreserveConversation: true),
            CancellationToken.None);

        allowFirstInitializeCompletion.TrySetResult(null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstApplyTask);

        firstCandidate.Raise(
            x => x.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-session-1", new PlanUpdate(title: "stale-one")));
        firstCandidate.Raise(
            x => x.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-session-1", new PlanUpdate(title: "stale-two")));
        dispatcher.RunAll();

        connectionCoordinator.Verify(
            x => x.ResyncAsync(sink, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConnectProfileInPoolAsync_DoesNotCallConnectionCoordinatorOrReplaceVisibleChatService()
    {
        var service = CreateChatService(new AgentCapabilities(loadSession: true));
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();
        var sink = new FakeSink
        {
            CurrentSessionId = "conv-a",
            CurrentRemoteSessionId = "remote-a",
            SelectedProfileId = "profile-a"
        };

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null))
            .Returns(service.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object);

        var profile = new ServerConfiguration
        {
            Id = "profile-b",
            Name = "Agent B",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe",
            StdioArgs = "--serve"
        };
        var transport = new FakeTransportConfiguration();

        var result = await sut.ConnectProfileInPoolAsync(profile, transport);

        Assert.IsType<AcpChatServiceAdapter>(result.ChatService);
        service.Verify(x => x.InitializeAsync(It.IsAny<InitializeParams>()), Times.Once);
        connectionCoordinator.Verify(x => x.SetConnectedAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        connectionCoordinator.Verify(x => x.SetConnectingAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        connectionCoordinator.Verify(x => x.SetInitializingAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(sink.ReplaceChatServiceCalls);
    }

    [Fact]
    public async Task ConnectProfileInPoolAsync_WhenCalledTwiceForSameProfile_ReusesExistingSession()
    {
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null))
            .Returns(service.Object);

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe",
            StdioArgs = "--serve"
        };
        var transport = new FakeTransportConfiguration();

        var first = await sut.ConnectProfileInPoolAsync(profile, transport);
        var second = await sut.ConnectProfileInPoolAsync(profile, transport);

        Assert.Same(first.ChatService, second.ChatService);
        service.Verify(x => x.InitializeAsync(It.IsAny<InitializeParams>()), Times.Once);
    }

    [Fact]
    public async Task DisconnectProfileInPoolAsync_RemovesSessionFromPoolAndDisposesService()
    {
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var registry = new InMemoryAcpConnectionSessionRegistry();

        factory
            .Setup(x => x.CreateChatService(TransportType.Stdio, "agent.exe", "--serve", null))
            .Returns(service.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            sessionRegistry: registry);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe",
            StdioArgs = "--serve"
        };
        var transport = new FakeTransportConfiguration();

        await sut.ConnectProfileInPoolAsync(profile, transport);
        Assert.True(registry.TryGetByProfile("profile-1", out _));

        await sut.DisconnectProfileInPoolAsync("profile-1");

        Assert.False(registry.TryGetByProfile("profile-1", out _));
        service.Verify(x => x.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task ApplyTransportConfigurationAsync_GenerationAdvanced_ReleasesBufferedUpdatesImmediately()
    {
        var transport = new FakeTransportConfiguration
        {
            SelectedTransportType = TransportType.WebSocket,
            RemoteUrl = "wss://agent.test"
        };
        var service = CreateChatService();
        var sink = new FakeSink
        {
            IsSessionActive = true,
            CurrentSessionId = "local-session-1",
            ConnectionGeneration = 1
        };
        var updates = new List<SessionUpdateEventArgs>();
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();

        factory
            .Setup(x => x.CreateChatService(TransportType.WebSocket, null, null, "wss://agent.test"))
            .Returns(service.Object);

        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object);

        await sut.ApplyTransportConfigurationAsync(transport, sink, preserveConversation: true);
        sink.CurrentChatService!.SessionUpdateReceived += (_, args) => updates.Add(args);

        service.Raise(
            x => x.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote-session-1", new PlanUpdate(title: "immediate")));

        Assert.Single(updates);
    }

    private static Mock<IChatService> CreateChatService(AgentCapabilities? agentCapabilities = null)
    {
        var service = new Mock<IChatService>();
        service.SetupGet(x => x.IsConnected).Returns(true);
        service.SetupGet(x => x.IsInitialized).Returns(true);
        service.SetupGet(x => x.SessionHistory).Returns(Array.Empty<SessionUpdateEntry>());
        service.Setup(x => x.DisconnectAsync()).ReturnsAsync(true);
        service.SetupGet(x => x.AgentCapabilities).Returns(agentCapabilities);
        service.Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent", "1.0.0"), agentCapabilities ?? new AgentCapabilities()));
        return service;
    }

    private sealed class RecordingConnectionPoolManager : IAcpConnectionPoolManager
    {
        public AcpConnectionDependencySnapshot? LastCleanupSnapshot { get; private set; }

        public Task<AcpConnectionSessionCleanupResult> CleanupBeforeApplyAsync(
            IChatService? activeService,
            AcpConnectionDependencySnapshot dependencySnapshot,
            CancellationToken cancellationToken = default)
        {
            LastCleanupSnapshot = dependencySnapshot;
            return Task.FromResult(new AcpConnectionSessionCleanupResult(0, 0));
        }

        public bool TryGetReusableSession(
            string? selectedProfileId,
            AcpConnectionReuseKey reuseKey,
            out AcpConnectionSession session)
        {
            session = default!;
            return false;
        }

        public void RecordSession(
            string profileId,
            AcpChatServiceAdapter service,
            InitializeResponse initializeResponse,
            AcpConnectionReuseKey reuseKey,
            string? connectionInstanceId)
        {
        }

        public bool RemoveByService(IChatService service, out string profileId)
        {
            profileId = string.Empty;
            return false;
        }

        public AcpConnectionPoolMetricsSnapshot GetMetricsSnapshot() => new(0, 0, 0, 0);
    }

    private sealed class FakeTransportConfiguration : IAcpTransportConfiguration
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public TransportType SelectedTransportType { get; set; }
        public string StdioCommand { get; set; } = string.Empty;
        public string StdioArgs { get; set; } = string.Empty;
        public string RemoteUrl { get; set; } = string.Empty;
        public bool IsValid { get; set; } = true;
        public string? ValidationError { get; set; }

        public (bool IsValid, string? ErrorMessage) Validate() => (IsValid, ValidationError);
    }

    private sealed class FakeSink : IAcpChatCoordinatorSink
    {
        public FakeSink()
        {
            BindingCommands = new FakeBindingCommands(this);
        }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public IChatService? CurrentChatService { get; set; }
        public IConversationBindingCommands ConversationBindingCommands => BindingCommands;
        public bool IsConnected { get; set; }
        public bool IsInitialized { get; set; }
        public bool IsConnecting { get; private set; }
        public bool IsInitializing { get; private set; }
        public bool IsSessionActive { get; set; }
        public bool IsHydrating { get; private set; }
        public bool IsAuthenticationRequired { get; private set; }
        public string? ConnectionErrorMessage { get; private set; }
        public string? AuthenticationHintMessage { get; private set; }
        public string? AgentName { get; private set; }
        public string? AgentVersion { get; private set; }
        public string? CurrentSessionId { get; set; }
        public string? CurrentRemoteSessionId { get; set; }
        public string? SelectedProfileId { get; set; }
        public string? ConnectionInstanceId { get; set; }
        public IUiDispatcher Dispatcher { get; set; } = new ImmediateUiDispatcher();
        public long ConnectionGeneration { get; set; }
        public string ActiveSessionCwd { get; set; } = string.Empty;
        public ConversationRemoteBindingState? ResolvedBinding { get; set; }
        public int ClearedRemoteSessionBindings { get; private set; }
        public List<(string RemoteSessionId, string? ProfileId, bool PreserveConversation)> BoundRemoteSessions { get; } = new();
        public List<IChatService?> ReplaceChatServiceCalls { get; } = new();
        public List<ServiceReplaceIntent> ReplaceChatServiceIntents { get; } = new();
        public FakeBindingCommands BindingCommands { get; }
        public int ResetHydratedConversationForResyncCalls { get; private set; }

        public ValueTask<ConversationRemoteBindingState?> GetCurrentRemoteBindingAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ResolvedBinding ?? (
                string.IsNullOrWhiteSpace(CurrentSessionId)
                    ? null
                    : new ConversationRemoteBindingState(CurrentSessionId!, CurrentRemoteSessionId, SelectedProfileId)));

        public void SelectProfile(ServerConfiguration profile)
        {
            SelectedProfileId = profile.Id;
        }

        public void ReplaceChatService(IChatService? chatService)
        {
            ReplaceChatServiceCalls.Add(chatService);
            CurrentChatService = chatService;
        }

        public Task ReplaceChatServiceAsync(IChatService? chatService, ServiceReplaceIntent intent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceChatServiceIntents.Add(intent);
            ReplaceChatService(chatService);
            return Task.CompletedTask;
        }

        public void UpdateConnectionState(bool isConnecting, bool isConnected, bool isInitialized, string? errorMessage)
        {
            IsConnecting = isConnecting;
            IsConnected = isConnected;
            IsInitialized = isInitialized;
            ConnectionErrorMessage = errorMessage;
        }

        public void UpdateInitializationState(bool isInitializing)
        {
            IsInitializing = isInitializing;
        }

        public void UpdateAuthenticationState(bool isRequired, string? hintMessage)
        {
            IsAuthenticationRequired = isRequired;
            AuthenticationHintMessage = hintMessage;
        }

        public void UpdateAgentIdentity(string? agentName, string? agentVersion)
        {
            AgentName = agentName;
            AgentVersion = agentVersion;
        }

        public Task ResetHydratedConversationForResyncAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetHydratedConversationForResyncCalls++;
            return Task.CompletedTask;
        }

        public Task SetIsHydratingAsync(bool isHydrating, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsHydrating = isHydrating;
            return Task.CompletedTask;
        }

        public void BindRemoteSession(string remoteSessionId, string? profileId, SessionNewResponse response, bool preserveConversation)
        {
            CurrentRemoteSessionId = remoteSessionId;
            BoundRemoteSessions.Add((remoteSessionId, profileId, preserveConversation));
        }

        public void ClearRemoteSessionBinding()
        {
            ClearedRemoteSessionBindings++;
            CurrentRemoteSessionId = null;
        }

        public string GetActiveSessionCwdOrDefault() => ActiveSessionCwd;
    }

    private sealed class FakeBindingCommands : IConversationBindingCommands
    {
        private readonly FakeSink _sink;

        public FakeBindingCommands(FakeSink sink)
        {
            _sink = sink;
        }

        public List<(string ConversationId, string? RemoteSessionId, string? ProfileId)> Updates { get; } = new();

        public int ClearCalls { get; private set; }

        public ValueTask<BindingUpdateResult> UpdateBindingAsync(string conversationId, string? remoteSessionId, string? boundProfileId)
        {
            Updates.Add((conversationId, remoteSessionId, boundProfileId));
            _sink.CurrentRemoteSessionId = remoteSessionId;
            if (string.IsNullOrWhiteSpace(remoteSessionId))
            {
                ClearCalls++;
            }
            return ValueTask.FromResult(BindingUpdateResult.Success());
        }
    }

    private sealed class QueueingUiDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _callbacks = new();

        public bool HasThreadAccess => true;

        public void Enqueue(Action action)
        {
            _callbacks.Enqueue(action);
        }

        public Task EnqueueAsync(Action action)
        {
            _callbacks.Enqueue(action);
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(Func<Task> function)
        {
            _callbacks.Enqueue(() => function().GetAwaiter().GetResult());
            return Task.CompletedTask;
        }

        public void RunAll()
        {
            while (_callbacks.Count > 0)
            {
                _callbacks.Dequeue()();
            }
        }
    }
}
