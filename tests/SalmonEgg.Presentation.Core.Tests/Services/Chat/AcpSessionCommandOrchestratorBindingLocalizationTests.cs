using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Localization;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services.Chat;

public sealed class AcpSessionCommandOrchestratorBindingLocalizationTests
{
    [Fact]
    public async Task EnsureRemoteSessionAsync_WhenBindingUpdateFails_LocalizesExceptionMessage()
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set(
            "zh-Hans",
            "ChatBinding_UpdateFailedWithStatus",
            "更新会话绑定失败（{0}）：{1}");
        localizer.Set("zh-Hans", "ChatBinding_UnknownError", "未知错误");

        var chatService = new Mock<IChatService>(MockBehavior.Strict);
        chatService.SetupGet(service => service.IsConnected).Returns(true);
        chatService.SetupGet(service => service.IsInitialized).Returns(true);
        chatService
            .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-1"));

        var bindingCommands = new Mock<IConversationBindingCommands>(MockBehavior.Strict);
        bindingCommands
            .Setup(commands => commands.UpdateBindingAsync(
                "local-1",
                "remote-1",
                "profile-1"))
            .ReturnsAsync(BindingUpdateResult.NotFound());

        var mcpResolver = new Mock<IAcpMcpServerResolver>(MockBehavior.Strict);
        mcpResolver
            .Setup(resolver => resolver.ResolveCurrentMcpServersAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<McpServer>());

        var sink = new Mock<IAcpChatCoordinatorSink>(MockBehavior.Strict);
        sink.SetupGet(s => s.CurrentChatService).Returns(chatService.Object);
        sink.SetupGet(s => s.IsSessionActive).Returns(true);
        sink.SetupGet(s => s.CurrentSessionId).Returns("local-1");
        sink.SetupGet(s => s.SelectedProfileId).Returns("profile-1");
        sink.SetupGet(s => s.ConversationBindingCommands).Returns(bindingCommands.Object);
        sink
            .Setup(s => s.GetConversationRemoteBindingAsync("local-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationRemoteBindingState?)null);
        sink.Setup(s => s.GetActiveSessionCwdOrDefault()).Returns("/tmp/work");
        sink
            .Setup(s => s.ResolveProfile("profile-1"))
            .Returns(new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Local",
                Transport = TransportType.Stdio,
                StdioCommand = "agent"
            });

        var orchestrator = new AcpSessionCommandOrchestrator(
            Mock.Of<ILogger<AcpSessionCommandOrchestrator>>(),
            mcpResolver.Object,
            localizer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.EnsureRemoteSessionAsync(
                sink.Object,
                authenticateAsync: _ => Task.FromResult(true),
                markHydrated: () => { },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("更新会话绑定失败（NotFound）：未知错误", exception.Message);
        bindingCommands.VerifyAll();
        chatService.VerifyAll();
    }

    [Fact]
    public async Task EnsureRemoteSessionAsync_WhenBindingUpdateFailsWithoutLocalizer_UsesEnglishFallback()
    {
        var chatService = new Mock<IChatService>(MockBehavior.Strict);
        chatService.SetupGet(service => service.IsConnected).Returns(true);
        chatService.SetupGet(service => service.IsInitialized).Returns(true);
        chatService
            .Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse("remote-1"));

        var bindingCommands = new Mock<IConversationBindingCommands>(MockBehavior.Strict);
        bindingCommands
            .Setup(commands => commands.UpdateBindingAsync(
                "local-1",
                "remote-1",
                "profile-1"))
            .ReturnsAsync(BindingUpdateResult.Error("store fault"));

        var mcpResolver = new Mock<IAcpMcpServerResolver>(MockBehavior.Strict);
        mcpResolver
            .Setup(resolver => resolver.ResolveCurrentMcpServersAsync(
                It.IsAny<IAcpChatCoordinatorSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<McpServer>());

        var sink = new Mock<IAcpChatCoordinatorSink>(MockBehavior.Strict);
        sink.SetupGet(s => s.CurrentChatService).Returns(chatService.Object);
        sink.SetupGet(s => s.IsSessionActive).Returns(true);
        sink.SetupGet(s => s.CurrentSessionId).Returns("local-1");
        sink.SetupGet(s => s.SelectedProfileId).Returns("profile-1");
        sink.SetupGet(s => s.ConversationBindingCommands).Returns(bindingCommands.Object);
        sink
            .Setup(s => s.GetConversationRemoteBindingAsync("local-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationRemoteBindingState?)null);
        sink.Setup(s => s.GetActiveSessionCwdOrDefault()).Returns("/tmp/work");
        sink
            .Setup(s => s.ResolveProfile("profile-1"))
            .Returns(new ServerConfiguration
            {
                Id = "profile-1",
                Name = "Local",
                Transport = TransportType.Stdio,
                StdioCommand = "agent"
            });

        var orchestrator = new AcpSessionCommandOrchestrator(
            Mock.Of<ILogger<AcpSessionCommandOrchestrator>>(),
            mcpResolver.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.EnsureRemoteSessionAsync(
                sink.Object,
                authenticateAsync: _ => Task.FromResult(true),
                markHydrated: () => { },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Failed to update conversation binding (Error): store fault", exception.Message);
    }

    [Fact]
    public async Task EnsureRemoteSessionAsync_WhenNoActiveLocalConversation_LocalizesExceptionMessage()
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set(
            "zh-Hans",
            "ChatSession_NoActiveLocalConversation",
            "没有可用于创建 ACP 会话的活动本地会话。");

        var chatService = new Mock<IChatService>(MockBehavior.Strict);
        chatService.SetupGet(service => service.IsConnected).Returns(true);
        chatService.SetupGet(service => service.IsInitialized).Returns(true);

        var sink = new Mock<IAcpChatCoordinatorSink>(MockBehavior.Strict);
        sink.SetupGet(s => s.CurrentChatService).Returns(chatService.Object);
        sink.SetupGet(s => s.IsSessionActive).Returns(false);
        sink.SetupGet(s => s.CurrentSessionId).Returns((string?)null);

        var orchestrator = new AcpSessionCommandOrchestrator(
            Mock.Of<ILogger<AcpSessionCommandOrchestrator>>(),
            Mock.Of<IAcpMcpServerResolver>(),
            localizer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.EnsureRemoteSessionAsync(
                sink.Object,
                authenticateAsync: _ => Task.FromResult(true),
                markHydrated: () => { },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("没有可用于创建 ACP 会话的活动本地会话。", exception.Message);
    }

    [Fact]
    public async Task EnsureRemoteSessionAsync_WhenServiceNotReady_LocalizesExceptionMessage()
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set(
            "zh-Hans",
            "ChatService_NotConnectedInitialized",
            "ACP 聊天服务未连接或未完成初始化。");

        var chatService = new Mock<IChatService>(MockBehavior.Strict);
        chatService.SetupGet(service => service.IsConnected).Returns(false);
        chatService.SetupGet(service => service.IsInitialized).Returns(false);

        var sink = new Mock<IAcpChatCoordinatorSink>(MockBehavior.Strict);
        sink.SetupGet(s => s.CurrentChatService).Returns(chatService.Object);

        var orchestrator = new AcpSessionCommandOrchestrator(
            Mock.Of<ILogger<AcpSessionCommandOrchestrator>>(),
            Mock.Of<IAcpMcpServerResolver>(),
            localizer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.EnsureRemoteSessionAsync(
                sink.Object,
                authenticateAsync: _ => Task.FromResult(true),
                markHydrated: () => { },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("ACP 聊天服务未连接或未完成初始化。", exception.Message);
    }
}
