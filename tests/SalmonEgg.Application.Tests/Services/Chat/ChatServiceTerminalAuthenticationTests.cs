using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Application.Tests.Services.Chat;

public sealed class ChatServiceTerminalAuthenticationTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task InitializeAsync_OnlyLocalInvocationWithSupportedHost_AdvertisesTerminal(bool local, bool supported, bool expected)
    {
        // Arrange
        var client = new Mock<IAcpClient>();
        InitializeParams? sent = null;
        client.Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>(), It.IsAny<CancellationToken>()))
            .Callback<InitializeParams, CancellationToken>((p, _) => sent = p)
            .ReturnsAsync(new InitializeResponse(1, new AgentInfo("agent", "1"), new AgentCapabilities()));
        var factory = new Mock<ITerminalAuthenticationSessionFactory>();
        factory.SetupGet(x => x.IsSupported).Returns(supported);
        using var service = new ChatService(client.Object, Mock.Of<IErrorLogger>(), new SessionManager(),
            local ? Mock.Of<IStdioInvocationSource>() : null, factory.Object);
        var capabilities = ClientCapabilityDefaults.Create();

        // Act
        await service.InitializeAsync(new InitializeParams(new ClientInfo("client", "1"), capabilities));

        // Assert
        Assert.Equal(expected, sent!.ClientCapabilities.Auth?.Terminal == true);
        Assert.Null(capabilities.Auth);
        Assert.Same(capabilities.Meta, sent.ClientCapabilities.Meta);
    }

    [Fact]
    public void Invocation_DelayedDecorator_TracksLiveSourceWithoutResolvingAgain()
    {
        // Arrange
        var snapshot = new StdioInvocationSnapshot("agent", ["--acp"], new Dictionary<string, string>(), "/work", true);
        var source = new Mock<IStdioInvocationSource>();
        source.SetupGet(x => x.StdioInvocation).Returns(snapshot);
        using var service = new ChatService(Mock.Of<IAcpClient>(), Mock.Of<IErrorLogger>(), new SessionManager(), source.Object);
        using var delayed = new DelayedLoadChatService(service, TimeSpan.FromMilliseconds(1));

        // Act / Assert
        Assert.Same(snapshot, ((IStdioInvocationSource)delayed).StdioInvocation);
        source.SetupGet(x => x.StdioInvocation).Returns((StdioInvocationSnapshot?)null);
        Assert.Null(((IStdioInvocationSource)delayed).StdioInvocation);
    }
}
