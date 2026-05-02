using System.Collections.ObjectModel;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Panels;

public sealed class ChatConversationPanelStateCoordinatorTests
{
    [Fact]
    public void SyncConversation_WithoutExistingState_CreatesDefaultTabsAndSelectsFirst()
    {
        var sut = new ChatConversationPanelStateCoordinator();

        var selection = sut.SyncConversation("conv-1");

        Assert.Equal(2, selection.Tabs.Count);
        Assert.Equal("terminal", selection.SelectedTab?.Id);
        Assert.Empty(selection.TerminalSessions);
        Assert.Null(selection.SelectedTerminal);
        Assert.Null(selection.PendingAskUserRequest);
    }

    [Fact]
    public void SelectTerminal_ForCurrentConversation_ReturnsSelectedTerminalSnapshot()
    {
        var sut = new ChatConversationPanelStateCoordinator();
        sut.SyncConversation("conv-1");
        var terminal = sut.GetOrCreateTerminalSession("conv-1", "term-1");

        var selection = sut.SelectTerminal("conv-1", terminal, isCurrentConversation: true);

        Assert.Single(selection.TerminalSessions);
        Assert.Same(terminal, selection.SelectedTerminal);
    }

    [Fact]
    public void StoreAskUserRequest_ExposesPendingRequestForConversation()
    {
        var sut = new ChatConversationPanelStateCoordinator();
        var request = new AskUserRequestViewModel("message-1", "remote-1", "prompt", []);

        sut.StoreAskUserRequest("conv-1", request);

        var selection = sut.SyncConversation("conv-1");
        Assert.Same(request, selection.PendingAskUserRequest);
    }

    [Fact]
    public void RemoveConversation_WhenCurrentConversation_ReturnsEmptySelection()
    {
        var sut = new ChatConversationPanelStateCoordinator();
        sut.SyncConversation("conv-1");
        var terminal = sut.GetOrCreateTerminalSession("conv-1", "term-1");
        sut.SelectTerminal("conv-1", terminal, isCurrentConversation: true);
        sut.StoreAskUserRequest("conv-1", new AskUserRequestViewModel("message-1", "remote-1", "prompt", []));

        var selection = sut.RemoveConversation("conv-1", isCurrentConversation: true);

        Assert.Empty(selection.Tabs);
        Assert.Empty(selection.TerminalSessions);
        Assert.Null(selection.SelectedTab);
        Assert.Null(selection.SelectedTerminal);
        Assert.Null(selection.PendingAskUserRequest);
    }
}
