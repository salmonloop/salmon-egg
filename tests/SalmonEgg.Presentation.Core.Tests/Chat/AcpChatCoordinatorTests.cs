using System;
using System.Collections.Generic;
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
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class AcpChatCoordinatorTests
{
    [Fact]
    public void IAcpChatCoordinatorSink_RequiresExplicitConversationBindingCommands()
    {
        var getter = typeof(IAcpChatCoordinatorSink)
            .GetProperty(nameof(IAcpChatCoordinatorSink.ConversationBindingCommands))?
            .GetMethod;

        Assert.NotNull(getter);
        Assert.True(getter!.IsAbstract);
    }

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
        service.Verify(x => x.InitializeAsync(It.IsAny<InitializeParams>()), Times.Once);
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
    public async Task SendPromptAsync_RemoteSessionNotFound_ClearsBindingRecreatesSessionAndRetriesOnce()
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
            ActiveSessionCwd = @"C:\repo\demo",
            SelectedProfileId = "profile-1"
        };
        var factory = new Mock<IAcpChatServiceFactory>();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var firstPrompt = true;

        service
            .Setup(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Returns<SessionPromptParams, CancellationToken>((parameters, _) =>
            {
                if (firstPrompt)
                {
                    firstPrompt = false;
                    Assert.Equal("remote-stale", parameters.SessionId);
                    throw new AcpException(JsonRpcErrorCode.ResourceNotFound, "session not found");
                }

                Assert.Equal("remote-session-2", parameters.SessionId);
                return Task.FromResult(new SessionPromptResponse());
            });

        service
            .Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-session-2"));

        var sut = new AcpChatCoordinator(factory.Object, logger.Object);

        var result = await sut.SendPromptAsync("hello", sink, _ => Task.FromResult(true));

        Assert.True(result.RetriedAfterSessionRecovery);
        Assert.Equal("remote-session-2", result.RemoteSessionId);
        Assert.Equal("remote-session-2", sink.CurrentRemoteSessionId);
        Assert.Equal(2, sink.BindingCommands.Updates.Count);
        Assert.Null(sink.BindingCommands.Updates[0].RemoteSessionId);
        Assert.Equal("remote-session-2", sink.BindingCommands.Updates[1].RemoteSessionId);
        service.Verify(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CancelPromptAsync_UsesCurrentRemoteSessionFromState()
    {
        var service = CreateChatService();
        var sink = new FakeSink
        {
            CurrentChatService = service.Object,
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
        var sut = new AcpChatCoordinator(
            factory.Object,
            logger.Object,
            connectionCoordinator: connectionCoordinator.Object);

        await sut.DisconnectAsync(sink);

        service.Verify(x => x.DisconnectAsync(), Times.Once);
        Assert.Null(sink.CurrentChatService);
        Assert.Null(sink.CurrentRemoteSessionId);
        Assert.Equal(1, sink.BindingCommands.ClearCalls);
        connectionCoordinator.Verify(
            x => x.ResetAsync(It.IsAny<CancellationToken>()),
            Times.Once);
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
        var sink = new FakeSink
        {
            IsSessionActive = true,
            CurrentSessionId = "local-session-1"
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

        connectionCoordinator.Verify(
            x => x.ResyncAsync(sink, It.IsAny<CancellationToken>()),
            Times.Once);
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

    private static Mock<IChatService> CreateChatService()
    {
        var service = new Mock<IChatService>();
        service.SetupGet(x => x.IsConnected).Returns(true);
        service.SetupGet(x => x.IsInitialized).Returns(true);
        service.SetupGet(x => x.SessionHistory).Returns(Array.Empty<SessionUpdateEntry>());
        service.Setup(x => x.DisconnectAsync()).ReturnsAsync(true);
        service.Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent", "1.0.0"), new AgentCapabilities()));
        return service;
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
        public bool IsAuthenticationRequired { get; private set; }
        public string? ConnectionErrorMessage { get; private set; }
        public string? AuthenticationHintMessage { get; private set; }
        public string? AgentName { get; private set; }
        public string? AgentVersion { get; private set; }
        public string? CurrentSessionId { get; set; }
        public string? CurrentRemoteSessionId { get; set; }
        public string? SelectedProfileId { get; set; }
        public SynchronizationContext SessionUpdateSynchronizationContext { get; set; } = new ImmediateSynchronizationContext();
        public long ConnectionGeneration { get; set; }
        public string ActiveSessionCwd { get; set; } = string.Empty;
        public int ClearedRemoteSessionBindings { get; private set; }
        public List<(string RemoteSessionId, string? ProfileId, bool PreserveConversation)> BoundRemoteSessions { get; } = new();
        public FakeBindingCommands BindingCommands { get; }

        public void SelectProfile(ServerConfiguration profile)
        {
            SelectedProfileId = profile.Id;
        }

        public void ReplaceChatService(IChatService? chatService)
        {
            CurrentChatService = chatService;
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

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            d(state);
        }
    }
}
