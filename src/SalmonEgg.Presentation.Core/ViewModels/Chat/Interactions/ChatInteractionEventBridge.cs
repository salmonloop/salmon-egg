using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;

namespace SalmonEgg.Presentation.ViewModels.Chat.Interactions;

public sealed class ChatInteractionEventBridge
{
    private readonly IAuthoritativeRemoteSessionRouter _authoritativeRemoteSessionRouter;
    private readonly ChatTerminalProjectionCoordinator _terminalProjectionCoordinator;

    public ChatInteractionEventBridge(
        IAuthoritativeRemoteSessionRouter authoritativeRemoteSessionRouter,
        ChatTerminalProjectionCoordinator terminalProjectionCoordinator)
    {
        _authoritativeRemoteSessionRouter = authoritativeRemoteSessionRouter ?? throw new ArgumentNullException(nameof(authoritativeRemoteSessionRouter));
        _terminalProjectionCoordinator = terminalProjectionCoordinator ?? throw new ArgumentNullException(nameof(terminalProjectionCoordinator));
    }

    public PermissionRequestViewModel CreatePermissionRequestViewModel(
        PermissionRequestEventArgs args,
        Func<object, string, string?, Task<bool>> respondAsync,
        Action dismiss)
        => ChatInteractionDialogFactory.CreatePermissionRequestViewModel(args, respondAsync, dismiss);

    public FileSystemRequestViewModel CreateFileSystemRequestViewModel(
        FileSystemRequestEventArgs args,
        Func<object, bool, string?, string?, Task> respondAsync,
        Action dismiss)
        => ChatInteractionDialogFactory.CreateFileSystemRequestViewModel(args, respondAsync, dismiss);

    public async Task<(string ConversationId, AskUserRequestViewModel ViewModel)?> BuildAskUserRequestAsync(
        AskUserRequestEventArgs args,
        Func<string, Task> clearPendingRequestAsync,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(clearPendingRequestAsync);
        ArgumentNullException.ThrowIfNull(logger);

        var conversationId = await _authoritativeRemoteSessionRouter.ResolveConversationIdAsync(args.SessionId).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            logger.LogWarning("Ask-user request ignored because no bound conversation matched remote session {RemoteSessionId}", args.SessionId);
            return null;
        }

        AskUserRequestViewModel? requestViewModel = null;
        requestViewModel = AskUserInteractionViewModelFactory.Create(
            args.Request,
            args.MessageId,
            async answers =>
            {
                var succeeded = await args.Respond(answers).ConfigureAwait(true);
                if (!succeeded)
                {
                    return false;
                }

                await clearPendingRequestAsync(conversationId).ConfigureAwait(true);
                return true;
            });

        return (conversationId, requestViewModel);
    }

    public async Task<(string ConversationId, ChatConversationPanelSelection Selection)?> BuildTerminalRequestSelectionAsync(
        TerminalRequestEventArgs args,
        ChatConversationPanelStateCoordinator panelStateCoordinator,
        string? currentConversationId,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(panelStateCoordinator);
        ArgumentNullException.ThrowIfNull(logger);

        var conversationId = await _authoritativeRemoteSessionRouter.ResolveConversationIdAsync(args.SessionId).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            logger.LogWarning("Terminal request ignored because no bound conversation matched remote session {RemoteSessionId}", args.SessionId);
            return null;
        }

        return _terminalProjectionCoordinator.TryApplyRequest(
            panelStateCoordinator,
            conversationId,
            args,
            string.Equals(currentConversationId, conversationId, StringComparison.Ordinal),
            out var selection)
                ? (conversationId, selection)
                : null;
    }

    public async Task<(string ConversationId, ChatConversationPanelSelection Selection)?> BuildTerminalStateSelectionAsync(
        TerminalStateChangedEventArgs args,
        ChatConversationPanelStateCoordinator panelStateCoordinator,
        string? currentConversationId,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(panelStateCoordinator);
        ArgumentNullException.ThrowIfNull(logger);

        var conversationId = await _authoritativeRemoteSessionRouter.ResolveConversationIdAsync(args.SessionId).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            logger.LogWarning("Terminal state ignored because no bound conversation matched remote session {RemoteSessionId}", args.SessionId);
            return null;
        }

        return _terminalProjectionCoordinator.TryApplyState(
            panelStateCoordinator,
            conversationId,
            args,
            string.Equals(currentConversationId, conversationId, StringComparison.Ordinal),
            out var selection)
                ? (conversationId, selection)
                : null;
    }
}
