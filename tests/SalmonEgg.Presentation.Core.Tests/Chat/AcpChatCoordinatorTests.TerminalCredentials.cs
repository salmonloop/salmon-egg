using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed partial class AcpChatCoordinatorTests
{
    [Fact]
    public async Task ConnectToProfileAsync_TerminalLoginForcesFreshCredentialSessionAndKeepsInvocationSource()
    {
        // Arrange
        var oldService = CreateChatService();
        var freshService = CreateChatService();
        var invocation = new StdioInvocationSnapshot("agent", ["--acp"],
            new Dictionary<string, string> { ["AGENT_TOKEN"] = "unchanged-secret" }, "/work", true);
        oldService.As<IStdioInvocationSource>().SetupGet(value => value.StdioInvocation).Returns(invocation);
        freshService.As<IStdioInvocationSource>().SetupGet(value => value.StdioInvocation).Returns(invocation);
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.SetupSequence(value => value.CreateChatService(It.IsAny<ServerConfiguration>()))
            .Returns(oldService.Object).Returns(freshService.Object);
        var coordinator = CreateCoordinator(factory.Object, NullLogger<AcpChatCoordinator>.Instance,
            CreateTransportSupportPolicy(), EmptyMcpServerProvider);
        var profile = CreateBoundProfile("unchanged-secret");
        var sink = new FakeSink();
        var first = await ConnectCredentialProfileAsync(coordinator, profile, sink, pooled: false);
        Assert.Same(invocation, Assert.IsAssignableFrom<IStdioInvocationSource>(first.ChatService).StdioInvocation);
        var context = new AcpConnectionContext(sink.CurrentSessionId, PreserveConversation: true)
        {
            ForceReconnect = true,
            ExpectedChatService = first.ChatService,
            ExpectedConnectionInstanceId = sink.ConnectionInstanceId,
            ExpectedProfileId = profile.Id
        };

        // Act: credentials and key are unchanged, but interactive login must bypass the old process.
        var second = await coordinator.ConnectToProfileAsync(profile, new FakeTransportConfiguration(), sink,
            context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotSame(first.ChatService, second.ChatService);
        Assert.Same(invocation, Assert.IsAssignableFrom<IStdioInvocationSource>(second.ChatService).StdioInvocation);
        Assert.True(Assert.IsType<AcpChatServiceAdapter>(second.ChatService)
            .UsesCredentialSnapshot(CredentialBindingResolver.Resolve(profile).Value));
        factory.Verify(value => value.CreateChatService(It.IsAny<ServerConfiguration>()), Times.Exactly(2));
        oldService.Verify(value => value.DisconnectAsync(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectToProfileAsync_StaleTerminalCallback_DoesNotCancelNewCredentialConnection(bool directTransportEntry)
    {
        // Arrange
        var firstService = CreateChatService();
        var newService = CreateChatService();
        var initializeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initializeRelease = new TaskCompletionSource<InitializeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        newService.Setup(value => value.InitializeAsync(It.IsAny<InitializeParams>())).Returns(() =>
        {
            initializeStarted.TrySetResult();
            return initializeRelease.Task;
        });
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.SetupSequence(value => value.CreateChatService(It.IsAny<ServerConfiguration>()))
            .Returns(firstService.Object).Returns(newService.Object);
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var coordinator = CreateCoordinator(factory.Object, NullLogger<AcpChatCoordinator>.Instance,
            CreateTransportSupportPolicy(), EmptyMcpServerProvider, sessionRegistry: registry);
        var sink = new FakeSink();
        var profile = CreateBoundProfile("old-secret");
        var first = await ConnectCredentialProfileAsync(coordinator, profile, sink, pooled: false);
        var oldContext = new AcpConnectionContext(sink.CurrentSessionId, PreserveConversation: true)
        {
            ForceReconnect = true,
            ExpectedChatService = first.ChatService,
            ExpectedConnectionInstanceId = sink.ConnectionInstanceId,
            ExpectedProfileId = profile.Id
        };
        var replacement = profile.Clone();
        replacement.Authentication!.Token = "new-secret";
        var connecting = ConnectCredentialProfileAsync(coordinator, replacement, sink, pooled: false);
        await initializeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        // This models a newer conversation selection without a third connection request.
        sink.CurrentSessionId = "new-conversation";

        try
        {
            // Act: an old auth completion must be rejected before it supersedes the new apply scope.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directTransportEntry
                ? coordinator.ApplyTransportConfigurationAsync(new FakeTransportConfiguration(), sink,
                    oldContext, TestContext.Current.CancellationToken)
                : coordinator.ConnectToProfileAsync(profile, new FakeTransportConfiguration(), sink,
                    oldContext, TestContext.Current.CancellationToken));
            initializeRelease.TrySetResult(new InitializeResponse(1, new AgentInfo("new", "1"), new AgentCapabilities()));
            var active = await connecting.WaitAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.NotSame(first.ChatService, active.ChatService);
            Assert.Same(active.ChatService, sink.CurrentChatService);
            Assert.True(registry.TryGetByProfile(profile.Id, out var session));
            Assert.True(session.Service.UsesCredentialSnapshot(CredentialBindingResolver.Resolve(replacement).Value));
            newService.Verify(value => value.Dispose(), Times.Never);
            factory.Verify(value => value.CreateChatService(It.IsAny<ServerConfiguration>()), Times.Exactly(2));
        }
        finally
        {
            initializeRelease.TrySetResult(new InitializeResponse(1, new AgentInfo("new", "1"), new AgentCapabilities()));
            try { await connecting.WaitAsync(TestContext.Current.CancellationToken); }
            catch (OperationCanceledException) { }
            await coordinator.DisconnectAsync(sink, TestContext.Current.CancellationToken);
        }
    }
}
