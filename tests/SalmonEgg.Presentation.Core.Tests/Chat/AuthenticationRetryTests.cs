using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class AuthenticationRetryTests
{
    [Fact]
    public async Task CreateSession_AuthenticationReplacesConnection_RetriesThroughNewService()
    {
        // Arrange
        var oldService = ReadyService();
        var newService = ReadyService();
        oldService.Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>())).ThrowsAsync(AuthRequired());
        newService.Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>())).ReturnsAsync(new SessionNewResponse("remote-new"));
        var sink = CreateSink(oldService.Object);
        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.EnsureRemoteSessionAsync(sink.Object, _ =>
        {
            sink.SetupGet(x => x.CurrentChatService).Returns(newService.Object);
            return Task.FromResult(true);
        }, () => { }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("remote-new", result.RemoteSessionId);
        oldService.Verify(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()), Times.Once);
        newService.Verify(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()), Times.Once);
    }

    [Fact]
    public async Task SendPrompt_AuthenticationReplacesConnection_UsesHydratedBindingAndNewService()
    {
        // Arrange
        var oldService = ReadyService();
        var newService = ReadyService();
        oldService.Setup(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>())).ThrowsAsync(AuthRequired());
        newService.Setup(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SessionPromptResponse());
        var sink = CreateSink(oldService.Object);
        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.DispatchPromptToRemoteSessionAsync("remote-old", "hello", sink.Object, _ =>
        {
            sink.SetupGet(x => x.CurrentChatService).Returns(newService.Object);
            sink.Setup(x => x.GetCurrentRemoteBindingAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConversationRemoteBindingState("conversation-1", "remote-loaded", "profile-1"));
            return Task.FromResult(true);
        }, (_, _, _, _) => throw new InvalidOperationException("Hydrated binding must be reused."), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("remote-loaded", result.RemoteSessionId);
        Assert.True(result.RetriedAfterSessionRecovery);
        oldService.Verify(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()), Times.Once);
        newService.Verify(x => x.SendPromptAsync(It.Is<SessionPromptParams>(p => p.SessionId == "remote-loaded"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retry_ConversationChangesWhileAuthenticating_DoesNotSendAgain(bool create)
    {
        // Arrange
        var service = ReadyService();
        service.Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>())).ThrowsAsync(AuthRequired());
        service.Setup(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>())).ThrowsAsync(AuthRequired());
        var sink = CreateSink(service.Object);
        var orchestrator = CreateOrchestrator();
        Task<bool> Authenticate(CancellationToken _)
        {
            sink.SetupGet(x => x.CurrentSessionId).Returns("conversation-2");
            return Task.FromResult(true);
        }

        // Act / Assert
        if (create)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.EnsureRemoteSessionAsync(
                sink.Object, Authenticate, () => { }, TestContext.Current.CancellationToken));
            service.Verify(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()), Times.Once);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.DispatchPromptToRemoteSessionAsync(
                "remote-old", "hello", sink.Object, Authenticate, (_, _, _, _) => throw new InvalidOperationException(), TestContext.Current.CancellationToken));
            service.Verify(x => x.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    private static AcpException AuthRequired() => new(JsonRpcErrorCode.AuthenticationRequired, "Sign in");

    private static Mock<IChatService> ReadyService()
    {
        var service = new Mock<IChatService>();
        service.SetupGet(x => x.IsInitialized).Returns(true);
        service.SetupGet(x => x.IsConnected).Returns(true);
        return service;
    }

    private static AcpSessionCommandOrchestrator CreateOrchestrator()
    {
        var mcp = new Mock<IAcpMcpServerResolver>();
        mcp.Setup(x => x.ResolveCurrentMcpServersAsync(It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<McpServer>());
        return new AcpSessionCommandOrchestrator(NullLogger<AcpSessionCommandOrchestrator>.Instance, mcp.Object);
    }

    private static Mock<IAcpChatCoordinatorSink> CreateSink(IChatService service)
    {
        var sink = new Mock<IAcpChatCoordinatorSink>();
        sink.SetupGet(x => x.CurrentChatService).Returns(service);
        sink.SetupGet(x => x.IsInitialized).Returns(true);
        sink.SetupGet(x => x.IsSessionActive).Returns(true);
        sink.SetupGet(x => x.CurrentSessionId).Returns("conversation-1");
        sink.SetupGet(x => x.SelectedProfileId).Returns("profile-1");
        sink.Setup(x => x.GetActiveSessionCwdOrDefault()).Returns("/work");
        sink.Setup(x => x.ResolveProfile("profile-1")).Returns(new ServerConfiguration { Id = "profile-1", Transport = TransportType.Stdio });
        var bindings = new Mock<IConversationBindingCommands>();
        bindings.Setup(x => x.UpdateBindingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(BindingUpdateResult.Success());
        sink.SetupGet(x => x.ConversationBindingCommands).Returns(bindings.Object);
        return sink;
    }
}
