using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class AcpAuthoritativeConnectionResolverTests
{
    [Fact]
    public void TryResolveReadyForegroundConnection_WhenRegistryHasMatchingSessionButForegroundReferenceIsStale_UsesRegistrySession()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var foregroundService = CreateConnectedChatService().Object;
        var authoritativeService = CreateAdapterService();
        registry.Upsert(new AcpConnectionSession(
            ProfileId: "profile-1",
            Service: authoritativeService,
            InitializeResponse: new InitializeResponse(),
            ConnectionReuseKey: new AcpConnectionReuseKey(TransportType.WebSocket, string.Empty, string.Empty, "ws://agent.example.com"),
            ConnectionInstanceId: "conn-1"));

        var sut = new AcpAuthoritativeConnectionResolver(registry);
        var state = new ChatConnectionState(
            Phase: ConnectionPhase.Connected,
            SelectedProfileIntentId: "profile-1",
            Error: null,
            IsAuthenticationRequired: false,
            AuthenticationHintMessage: null,
            Generation: 1,
            ConnectionInstanceId: "conn-1",
            ForegroundTransportProfileId: "profile-1");

        var resolved = sut.TryResolveReadyForegroundConnection(
            foregroundService,
            state,
            requiredProfileId: "profile-1",
            out var snapshot);

        Assert.True(resolved);
        Assert.Same(authoritativeService, snapshot.ChatService);
        Assert.Equal("profile-1", snapshot.ProfileId);
        Assert.Equal("conn-1", snapshot.ConnectionInstanceId);
    }

    [Fact]
    public void TryResolveReadyForegroundConnection_WhenRegistrySessionConnectionInstanceDiffers_ReturnsFalse()
    {
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var foregroundService = CreateConnectedChatService().Object;
        registry.Upsert(new AcpConnectionSession(
            ProfileId: "profile-1",
            Service: CreateAdapterService(),
            InitializeResponse: new InitializeResponse(),
            ConnectionReuseKey: new AcpConnectionReuseKey(TransportType.WebSocket, string.Empty, string.Empty, "ws://agent.example.com"),
            ConnectionInstanceId: "conn-2"));

        var sut = new AcpAuthoritativeConnectionResolver(registry);
        var state = new ChatConnectionState(
            Phase: ConnectionPhase.Connected,
            SelectedProfileIntentId: "profile-1",
            Error: null,
            IsAuthenticationRequired: false,
            AuthenticationHintMessage: null,
            Generation: 1,
            ConnectionInstanceId: "conn-1",
            ForegroundTransportProfileId: "profile-1");

        var resolved = sut.TryResolveReadyForegroundConnection(
            foregroundService,
            state,
            requiredProfileId: "profile-1",
            out _);

        Assert.False(resolved);
    }

    private static Mock<IChatService> CreateConnectedChatService()
    {
        var chatService = new Mock<IChatService>();
        chatService.SetupGet(service => service.IsConnected).Returns(true);
        chatService.SetupGet(service => service.IsInitialized).Returns(true);
        return chatService;
    }

    private static AcpChatServiceAdapter CreateAdapterService()
        => new(
            CreateConnectedChatService().Object,
            new AcpEventAdapter(
                _ => { },
                new ImmediateUiDispatcher()));
}
