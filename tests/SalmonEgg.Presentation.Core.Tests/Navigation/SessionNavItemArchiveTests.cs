using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.Navigation;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class SessionNavItemArchiveTests
{
    [Fact]
    public async Task ArchiveAsync_WhenConfirmedAndFails_UsesEnglishDialogAndErrorCopy()
    {
        string? title = null;
        string? message = null;
        string? primary = null;
        string? close = null;
        string? info = null;

        var ui = new Mock<IUiInteractionService>();
        ui.Setup(u => u.ConfirmAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback((string t, string m, string p, string c) =>
            {
                title = t;
                message = m;
                primary = p;
                close = c;
            })
            .ReturnsAsync(true);
        ui.Setup(u => u.ShowInfoAsync(It.IsAny<string>()))
            .Callback((string value) => info = value)
            .Returns(Task.CompletedTask);

        var catalog = new Mock<IChatSessionCatalog>();
        catalog.Setup(c => c.ArchiveConversationAsync("session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationMutationResult(false, false, "failed"));

        var item = new SessionNavItemViewModel(
            sessionId: "session-1",
            projectId: "project-1",
            title: "Demo Session",
            relativeTimeText: "now",
            ui: ui.Object,
            chatSessionCatalog: catalog.Object,
            navigationState: Mock.Of<INavigationPaneState>(),
            uiDispatcher: new ImmediateUiDispatcher());

        await item.ArchiveCommand.ExecuteAsync(null);

        Assert.Equal("Archive session", title);
        Assert.Equal("Archive session \"Demo Session\"?", message);
        Assert.Equal("Archive", primary);
        Assert.Equal("Cancel", close);
        Assert.Equal("Failed to archive the session. Please try again later.", info);
    }
}
