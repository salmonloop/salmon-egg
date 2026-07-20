using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class SessionNavItemViewModelTests
{
    [Fact]
    public async Task CopySessionIdCommand_CopiesAcpSessionId_NotLocalConversationId()
    {
        var shell = new RecordingPlatformShellService();
        var item = CreateItem(
            new RecordingUiInteractionService(),
            new RecordingChatSessionCatalog(),
            remoteSessionId: "remote-session-42",
            shell: shell);

        await item.CopySessionIdCommand.ExecuteAsync(null);

        Assert.Equal("remote-session-42", shell.LastCopiedText);
    }

    [Fact]
    public async Task CopySessionIdCommand_WithoutAcpSessionId_DoesNotWriteClipboard()
    {
        var shell = new RecordingPlatformShellService();
        var item = CreateItem(
            new RecordingUiInteractionService(),
            new RecordingChatSessionCatalog(),
            remoteSessionId: null,
            shell: shell);

        await item.CopySessionIdCommand.ExecuteAsync(null);

        Assert.Null(shell.LastCopiedText);
    }

    [Fact]
    public void SessionNavItemViewModel_DoesNotExposeMoveCommand()
    {
        Assert.Null(typeof(SessionNavItemViewModel).GetProperty("MoveCommand"));
    }

    [Fact]
    public async Task ArchiveCommand_OnFailure_ShowsUserFeedback()
    {
        var ui = new RecordingUiInteractionService();
        var catalog = new RecordingChatSessionCatalog
        {
            ArchiveResult = new ConversationMutationResult(false, false, "ConversationRemovalPersistFailed"),
        };
        var item = CreateItem(ui, catalog);

        await item.ArchiveCommand.ExecuteAsync(null);

        Assert.Single(ui.InfoMessages);
        Assert.Contains("归档", ui.InfoMessages[0]);
    }

    [Fact]
    public async Task ArchiveCommand_OnSuccess_DoesNotShowFeedback()
    {
        var ui = new RecordingUiInteractionService();
        var catalog = new RecordingChatSessionCatalog();
        var item = CreateItem(ui, catalog);

        await item.ArchiveCommand.ExecuteAsync(null);

        Assert.Empty(ui.InfoMessages);
    }

    private static SessionNavItemViewModel CreateItem(
        IUiInteractionService ui,
        IChatSessionCatalog chatSessionCatalog,
        string? remoteSessionId = null,
        IPlatformShellService? shell = null)
        => new(
            sessionId: "session-1",
            remoteSessionId: remoteSessionId,
            projectId: "project-1",
            title: "Session 1",
            relativeTimeText: "刚刚",
            ui: ui,
            shell: shell ?? new RecordingPlatformShellService(),
            chatSessionCatalog: chatSessionCatalog,
            navigationState: new FakeNavigationPaneState(), uiDispatcher: new SalmonEgg.Presentation.Core.Tests.Threading.ImmediateUiDispatcher());

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
        public List<string> InfoMessages { get; } = new();

        public bool ConfirmResult { get; set; } = true;

        public bool CanPickFolder => false;

        public Task ShowInfoAsync(string message)
        {
            InfoMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText, string closeButtonText)
            => Task.FromResult(ConfirmResult);

        public Task<string?> PromptTextAsync(string title, string primaryButtonText, string closeButtonText, string initialText)
            => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);

        public Task<RemoteProjectSelectionResult> ShowRemoteProjectSelectionAsync(RemoteProjectSelectionViewModel viewModel)
            => Task.FromResult(RemoteProjectSelectionResult.Cancel);

        public Task ShowSessionsListDialogAsync(string title, IReadOnlyList<SessionNavItemViewModel> sessions, Action<string> onPickSession)
            => Task.CompletedTask;
    }

    private sealed class RecordingChatSessionCatalog : IChatSessionCatalog
    {
        public ConversationMutationResult ArchiveResult { get; set; } = new(true, false, null);

        public bool IsConversationListLoading => false;

        public int ConversationListVersion => 0;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public string[] GetKnownConversationIds() => [];

        public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ConversationMutationResult> ArchiveConversationAsync(string conversationId, CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveResult);

        public Task<ConversationMutationResult> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveResult);

    }

    private sealed class RecordingPlatformShellService : IPlatformShellService
    {
        public string? LastCopiedText { get; private set; }

        public Task<bool> OpenFolderAsync(string path) => Task.FromResult(false);

        public Task<bool> OpenFileAsync(string path) => Task.FromResult(false);

        public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);

        public Task<bool> CopyToClipboardAsync(string text)
        {
            LastCopiedText = text;
            return Task.FromResult(true);
        }

        public Task<string?> ReadClipboardTextAsync() => Task.FromResult<string?>(null);
    }
}
