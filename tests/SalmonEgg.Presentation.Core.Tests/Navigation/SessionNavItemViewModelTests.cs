using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class SessionNavItemViewModelTests
{
    [Fact]
    public async Task MoveCommand_PicksTargetAndMovesConversation()
    {
        var ui = new RecordingUiInteractionService
        {
            NextProjectId = "project-2"
        };
        var catalog = new RecordingChatSessionCatalog(
            [
                new ConversationProjectTargetOption(NavigationProjectIds.Unclassified, "未归类"),
                new ConversationProjectTargetOption("project-1", "Project One"),
                new ConversationProjectTargetOption("project-2", "Project Two")
            ]);
        var item = CreateItem(ui, catalog);

        await item.MoveCommand.ExecuteAsync(null);

        Assert.Equal("移动会话", ui.PickDialogTitle);
        Assert.Equal("Session 1", ui.PickSessionTitle);
        Assert.Equal("project-1", ui.PickSelectedProjectId);
        Assert.Collection(
            ui.PickOptions,
            option => Assert.Equal(NavigationProjectIds.Unclassified, option.ProjectId),
            option => Assert.Equal("project-1", option.ProjectId),
            option => Assert.Equal("project-2", option.ProjectId));
        Assert.Equal(("session-1", "project-2"), catalog.LastMoveRequest);
    }

    [Fact]
    public async Task MoveCommand_Cancelled_DoesNotMoveConversation()
    {
        var ui = new RecordingUiInteractionService();
        var catalog = new RecordingChatSessionCatalog(
            [
                new ConversationProjectTargetOption(NavigationProjectIds.Unclassified, "未归类"),
                new ConversationProjectTargetOption("project-1", "Project One")
            ]);
        var item = CreateItem(ui, catalog);

        await item.MoveCommand.ExecuteAsync(null);

        Assert.Null(catalog.LastMoveRequest);
    }

    [Fact]
    public async Task MoveCommand_WithoutTargets_DoesNotOpenPicker()
    {
        var ui = new RecordingUiInteractionService();
        var catalog = new RecordingChatSessionCatalog([]);
        var item = CreateItem(ui, catalog);

        await item.MoveCommand.ExecuteAsync(null);

        Assert.False(ui.PickConversationProjectCalled);
        Assert.Null(catalog.LastMoveRequest);
    }

    [Fact]
    public async Task MoveCommand_TrimsPickedProjectIdBeforeMove()
    {
        var ui = new RecordingUiInteractionService
        {
            NextProjectId = "  project-2  "
        };
        var catalog = new RecordingChatSessionCatalog(
            [
                new ConversationProjectTargetOption("project-1", "Project One"),
                new ConversationProjectTargetOption("project-2", "Project Two")
            ]);
        var item = CreateItem(ui, catalog);

        await item.MoveCommand.ExecuteAsync(null);

        Assert.Equal(("session-1", "project-2"), catalog.LastMoveRequest);
    }

    private static SessionNavItemViewModel CreateItem(
        IUiInteractionService ui,
        IChatSessionCatalog chatSessionCatalog)
        => new(
            sessionId: "session-1",
            projectId: "project-1",
            title: "Session 1",
            relativeTimeText: "刚刚",
            ui: ui,
            chatSessionCatalog: chatSessionCatalog,
            navigationState: new FakeNavigationPaneState());

    private sealed class FakeNavigationPaneState : INavigationPaneState
    {
        public bool IsPaneOpen => true;

        public event EventHandler? PaneStateChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class RecordingUiInteractionService : IUiInteractionService
    {
        public string? NextProjectId { get; init; }

        public bool PickConversationProjectCalled { get; private set; }

        public string PickDialogTitle { get; private set; } = string.Empty;

        public string PickSessionTitle { get; private set; } = string.Empty;

        public string? PickSelectedProjectId { get; private set; }

        public IReadOnlyList<ConversationProjectTargetOption> PickOptions { get; private set; } =
            Array.Empty<ConversationProjectTargetOption>();

        public Task ShowInfoAsync(string message) => Task.CompletedTask;

        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText, string closeButtonText)
            => Task.FromResult(false);

        public Task<string?> PromptTextAsync(string title, string primaryButtonText, string closeButtonText, string initialText)
            => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);

        public Task ShowSessionsListDialogAsync(string title, IReadOnlyList<SessionNavItemViewModel> sessions, Action<string> onPickSession)
            => Task.CompletedTask;

        public Task<string?> PickConversationProjectAsync(
            string title,
            string sessionTitle,
            IReadOnlyList<ConversationProjectTargetOption> options,
            string? selectedProjectId)
        {
            PickConversationProjectCalled = true;
            PickDialogTitle = title;
            PickSessionTitle = sessionTitle;
            PickOptions = options;
            PickSelectedProjectId = selectedProjectId;
            return Task.FromResult(NextProjectId);
        }
    }

    private sealed class RecordingChatSessionCatalog : IChatSessionCatalog
    {
        private readonly IReadOnlyList<ConversationProjectTargetOption> _projectTargets;

        public RecordingChatSessionCatalog(IReadOnlyList<ConversationProjectTargetOption> projectTargets)
        {
            _projectTargets = projectTargets;
        }

        public bool IsConversationListLoading => false;

        public int ConversationListVersion => 0;

        public (string SessionId, string ProjectId)? LastMoveRequest { get; private set; }

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

        public IReadOnlyList<ConversationProjectTargetOption> GetConversationProjectTargets() => _projectTargets;

        public void MoveConversationToProject(string conversationId, string projectId)
        {
            LastMoveRequest = (conversationId, projectId);
        }
    }
}
