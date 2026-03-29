using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class NavigationSelectionProjectorTests
{
    [Fact]
    public void Project_UsesSessionControlSelection_WhenPaneIsOpen()
    {
        var navState = new FakeNavigationPaneState(isPaneOpen: true);
        var start = new StartNavItemViewModel(navState);
        var project = new ProjectNavItemViewModel(
            new ProjectDefinition { ProjectId = "project-1", Name = "Demo", RootPath = @"C:\repo\demo" },
            isSystemProject: false,
            createSessionAsync: _ => Task.CompletedTask,
            navigationState: navState);
        var session = new SessionNavItemViewModel(
            sessionId: "session-1",
            projectId: "project-1",
            title: "Session 1",
            relativeTimeText: "刚刚",
            ui: new NoopUiInteractionService(),
            chatSessionCatalog: new FakeChatSessionCatalog(),
            navigationState: navState);

        var projector = new NavigationSelectionProjector();
        var projection = projector.Project(
            new NavigationSelectionState.Session("session-1"),
            start,
            new DiscoverSessionsNavItemViewModel(navState),
            new Dictionary<string, SessionNavItemViewModel> { ["session-1"] = session },
            new Dictionary<string, ProjectNavItemViewModel> { ["project-1"] = project },
            isPaneOpen: true);

        Assert.Same(session, projection.ControlSelectedItem);
        Assert.Contains("project-1", projection.ActiveProjectIds);
        Assert.Contains("session-1", projection.SelectedSessionIds);
        Assert.False(projection.IsSettingsSelected);
    }

    [Fact]
    public void Project_KeepsSessionControlSelection_WhenPaneIsClosed()
    {
        var navState = new FakeNavigationPaneState(isPaneOpen: false);
        var start = new StartNavItemViewModel(navState);
        var project = new ProjectNavItemViewModel(
            new ProjectDefinition { ProjectId = "project-1", Name = "Demo", RootPath = @"C:\repo\demo" },
            isSystemProject: false,
            createSessionAsync: _ => Task.CompletedTask,
            navigationState: navState);
        var session = new SessionNavItemViewModel(
            sessionId: "session-1",
            projectId: "project-1",
            title: "Session 1",
            relativeTimeText: "刚刚",
            ui: new NoopUiInteractionService(),
            chatSessionCatalog: new FakeChatSessionCatalog(),
            navigationState: navState);

        var projector = new NavigationSelectionProjector();
        var projection = projector.Project(
            new NavigationSelectionState.Session("session-1"),
            start,
            new DiscoverSessionsNavItemViewModel(navState),
            new Dictionary<string, SessionNavItemViewModel> { ["session-1"] = session },
            new Dictionary<string, ProjectNavItemViewModel> { ["project-1"] = project },
            isPaneOpen: false);

        Assert.Same(session, projection.ControlSelectedItem);
        Assert.Contains("project-1", projection.ActiveProjectIds);
        Assert.Contains("session-1", projection.SelectedSessionIds);
    }

    private sealed class FakeNavigationPaneState : INavigationPaneState
    {
        public FakeNavigationPaneState(bool isPaneOpen)
        {
            IsPaneOpen = isPaneOpen;
        }

        public bool IsPaneOpen { get; private set; }

        public event EventHandler? PaneStateChanged;

        public void SetPaneOpen(bool isPaneOpen)
        {
            if (IsPaneOpen == isPaneOpen)
            {
                return;
            }

            IsPaneOpen = isPaneOpen;
            PaneStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class NoopUiInteractionService : IUiInteractionService
    {
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
        public Task ShowInfoAsync(string message) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消") => Task.FromResult(false);
        public Task<string?> PromptTextAsync(string title, string primaryButtonText, string closeButtonText, string initialText) => Task.FromResult<string?>(null);
        public Task ShowSessionsListDialogAsync(string title, IReadOnlyList<SessionNavItemViewModel> sessions, Action<string> onPickSession) => Task.CompletedTask;
        public Task<string?> PickConversationProjectAsync(string title, string sessionTitle, IReadOnlyList<ConversationProjectTargetOption> options, string? selectedProjectId)
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeChatSessionCatalog : IChatSessionCatalog
    {
        public bool IsConversationListLoading => false;

        public int ConversationListVersion => 0;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public string[] GetKnownConversationIds() => [];

        public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void RenameConversation(string conversationId, string newDisplayName)
        {
        }

        public void ArchiveConversation(string conversationId)
        {
        }

        public void DeleteConversation(string conversationId)
        {
        }

        public IReadOnlyList<ConversationProjectTargetOption> GetConversationProjectTargets() => [];

        public void MoveConversationToProject(string conversationId, string projectId)
        {
        }
    }
}
