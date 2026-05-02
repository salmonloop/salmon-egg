using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Interactions;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Interactions;

public sealed class ChatInteractionEventBridgeTests
{
    [Fact]
    public async Task BuildAskUserRequestAsync_WhenConversationResolves_ReturnsViewModel()
    {
        var router = new Mock<IAuthoritativeRemoteSessionRouter>();
        router.Setup(x => x.ResolveConversationIdAsync("remote-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("conversation-1");
        var sut = new ChatInteractionEventBridge(router.Object, new ChatTerminalProjectionCoordinator());

        var result = await sut.BuildAskUserRequestAsync(
            new AskUserRequestEventArgs(
                "message-1",
                new AskUserRequest
                {
                    SessionId = "remote-1",
                    Questions =
                    {
                        new AskUserQuestion
                        {
                            Header = "Execution",
                            Question = "Choose",
                            Options = { new AskUserOption { Label = "Plan", Description = "Planning mode" } }
                        }
                    }
                },
                _ => Task.FromResult(true)),
            _ => Task.CompletedTask,
            NullLogger.Instance);

        Assert.NotNull(result);
        Assert.Equal("conversation-1", result.Value.ConversationId);
        Assert.Contains("Choose", result.Value.ViewModel.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildTerminalRequestSelectionAsync_WhenConversationResolves_ReturnsSelection()
    {
        var router = new Mock<IAuthoritativeRemoteSessionRouter>();
        router.Setup(x => x.ResolveConversationIdAsync("remote-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("conversation-1");
        var panelStateCoordinator = new ChatConversationPanelStateCoordinator();
        var sut = new ChatInteractionEventBridge(router.Object, new ChatTerminalProjectionCoordinator());
        using var payload = JsonDocument.Parse("""{"output":"hello"}""");

        var result = await sut.BuildTerminalRequestSelectionAsync(
            new TerminalRequestEventArgs("message-1", "remote-1", "terminal-1", "terminal/create", payload.RootElement, _ => Task.FromResult(true)),
            panelStateCoordinator,
            "conversation-1",
            NullLogger.Instance);

        Assert.NotNull(result);
        Assert.Equal("conversation-1", result.Value.ConversationId);
        Assert.Single(result.Value.Selection.TerminalSessions);
    }
}
