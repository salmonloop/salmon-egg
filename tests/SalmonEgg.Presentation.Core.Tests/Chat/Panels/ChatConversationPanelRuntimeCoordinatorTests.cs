using Moq;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Panels;

public sealed class ChatConversationPanelRuntimeCoordinatorTests
{
    [Fact]
    public void ResolveLocalTerminalSessionInfoCwd_PrefersSessionManagerValue()
    {
        var coordinator = new ChatConversationPanelRuntimeCoordinator();
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(x => x.GetSession("conversation-1"))
            .Returns(new Session { SessionId = "conversation-1", Cwd = "C:\\repo" });

        var cwd = coordinator.ResolveLocalTerminalSessionInfoCwd(sessionManager.Object, "conversation-1", ChatState.Empty);

        Assert.Equal("C:\\repo", cwd);
    }

    [Fact]
    public void SyncConversation_DelegatesToPanelStateCoordinator()
    {
        var coordinator = new ChatConversationPanelRuntimeCoordinator();
        var panelCoordinator = new ChatConversationPanelStateCoordinator();

        var selection = coordinator.SyncConversation(panelCoordinator, null);

        Assert.Empty(selection.Tabs);
        Assert.Empty(selection.TerminalSessions);
    }
}
