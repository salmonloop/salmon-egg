using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Navigation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class NavigationSelectionProjectionPolicyTests
{
    [Fact]
    public void ResolveProjectedSelection_ProjectsLatestChatActivation_WhenSessionCanProject()
    {
        var selection = NavigationSelectionProjectionPolicy.ResolveProjectedSelection(
            NavigationSelectionState.StartSelection,
            new SessionActivationSnapshot(
                "session-1",
                "project-1",
                Version: 7,
                SessionActivationPhase.SelectingConversation),
            ShellNavigationContent.Chat,
            latestActivationToken: 7,
            canProjectSession: sessionId => sessionId == "session-1");

        var session = Assert.IsType<NavigationSelectionState.Session>(selection);
        Assert.Equal("session-1", session.SessionId);
    }

    [Fact]
    public void ResolveProjectedSelection_ReturnsCommittedSelection_WhenPendingContentIsNotChat()
    {
        var selection = NavigationSelectionProjectionPolicy.ResolveProjectedSelection(
            NavigationSelectionState.StartSelection,
            new SessionActivationSnapshot(
                "session-1",
                "project-1",
                Version: 7,
                SessionActivationPhase.SelectingConversation),
            ShellNavigationContent.Start,
            latestActivationToken: 7,
            canProjectSession: _ => true);

        Assert.Same(NavigationSelectionState.StartSelection, selection);
    }

    [Fact]
    public void ResolveProjectedSelection_ReturnsCommittedSelection_WhenActivationIsStale()
    {
        var selection = NavigationSelectionProjectionPolicy.ResolveProjectedSelection(
            NavigationSelectionState.StartSelection,
            new SessionActivationSnapshot(
                "session-1",
                "project-1",
                Version: 6,
                SessionActivationPhase.SelectingConversation),
            ShellNavigationContent.Chat,
            latestActivationToken: 7,
            canProjectSession: _ => true);

        Assert.Same(NavigationSelectionState.StartSelection, selection);
    }

    [Theory]
    [InlineData(SessionActivationPhase.None)]
    [InlineData(SessionActivationPhase.Hydrated)]
    [InlineData(SessionActivationPhase.Faulted)]
    public void ResolveProjectedSelection_ReturnsCommittedSelection_ForTerminalActivationPhase(SessionActivationPhase phase)
    {
        var selection = NavigationSelectionProjectionPolicy.ResolveProjectedSelection(
            NavigationSelectionState.StartSelection,
            new SessionActivationSnapshot(
                "session-1",
                "project-1",
                Version: 7,
                phase),
            ShellNavigationContent.Chat,
            latestActivationToken: 7,
            canProjectSession: _ => true);

        Assert.Same(NavigationSelectionState.StartSelection, selection);
    }

    [Fact]
    public void ResolveProjectedSelection_ReturnsCommittedSelection_WhenSessionCannotProject()
    {
        var selection = NavigationSelectionProjectionPolicy.ResolveProjectedSelection(
            NavigationSelectionState.StartSelection,
            new SessionActivationSnapshot(
                "session-1",
                "project-1",
                Version: 7,
                SessionActivationPhase.SelectingConversation),
            ShellNavigationContent.Chat,
            latestActivationToken: 7,
            canProjectSession: _ => false);

        Assert.Same(NavigationSelectionState.StartSelection, selection);
    }

    [Fact]
    public void ResolveSelectionSessionId_ReturnsOnlyNonBlankSessionSelection()
    {
        Assert.Null(NavigationSelectionProjectionPolicy.ResolveSelectionSessionId(NavigationSelectionState.StartSelection));
        Assert.Null(NavigationSelectionProjectionPolicy.ResolveSelectionSessionId(new NavigationSelectionState.Session(string.Empty)));
        Assert.Equal(
            "session-1",
            NavigationSelectionProjectionPolicy.ResolveSelectionSessionId(new NavigationSelectionState.Session("session-1")));
    }
}
