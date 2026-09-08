using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;
using SalmonEgg.Acp.Client;

namespace SalmonEgg.Presentation.ViewModels.Chat.Interactions;

public sealed class ChatInteractionEventBridge
{
    private readonly IAuthoritativeRemoteSessionRouter _authoritativeRemoteSessionRouter;
    private readonly ChatTerminalProjectionCoordinator _terminalProjectionCoordinator;
    private readonly IStringLocalizer<CoreStrings>? _localizer;

    public ChatInteractionEventBridge(
        IAuthoritativeRemoteSessionRouter authoritativeRemoteSessionRouter,
        ChatTerminalProjectionCoordinator terminalProjectionCoordinator,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        _authoritativeRemoteSessionRouter = authoritativeRemoteSessionRouter ?? throw new ArgumentNullException(nameof(authoritativeRemoteSessionRouter));
        _terminalProjectionCoordinator = terminalProjectionCoordinator ?? throw new ArgumentNullException(nameof(terminalProjectionCoordinator));
        _localizer = localizer;
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
            },
            _localizer);

        return (conversationId, requestViewModel);
    }

    public async Task<(string ConversationId, ElicitationRequestViewModel ViewModel)?> BuildElicitationRequestAsync(
        ElicitationRequestEventArgs args,
        Func<string, ElicitationRequestViewModel, Task> clearPendingRequestAsync,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(clearPendingRequestAsync);
        ArgumentNullException.ThrowIfNull(logger);

        if (args.Request is not FormElicitationRequest || string.IsNullOrWhiteSpace(args.SessionId))
        {
            logger.LogWarning(
                "Elicitation request cannot be displayed because it is not a session-scoped form. Mode={ElicitationMode}",
                args.Request.Mode);
            await CancelUndisplayedElicitationAsync(args, logger).ConfigureAwait(false);
            return null;
        }

        string? conversationId;
        try
        {
            conversationId = await _authoritativeRemoteSessionRouter
                .ResolveConversationIdAsync(args.SessionId)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Elicitation request could not be routed to its conversation");
            await CancelUndisplayedElicitationAsync(args, logger).ConfigureAwait(false);
            return null;
        }

        if (string.IsNullOrWhiteSpace(conversationId))
        {
            logger.LogWarning(
                "Elicitation request cannot be displayed because no bound conversation matched remote session {RemoteSessionId}",
                args.SessionId);
            await CancelUndisplayedElicitationAsync(args, logger).ConfigureAwait(false);
            return null;
        }

        return (
            conversationId,
            ElicitationInteractionViewModelFactory.Create(
                args,
                viewModel => clearPendingRequestAsync(conversationId, viewModel),
                _localizer));
    }

    internal static async Task CancelUndisplayedElicitationAsync(ElicitationRequestEventArgs args, ILogger logger)
    {
        // Request scope is valid ACP. This conversation surface cannot present it, so dismiss it
        // explicitly instead of inventing a session or leaving the peer waiting for user input.
        try
        {
            if (!await args.Cancel().ConfigureAwait(false))
            {
                logger.LogWarning("Could not send cancellation for undisplayed elicitation request {MessageId}", args.MessageId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not cancel undisplayed elicitation request {MessageId}", args.MessageId);
        }
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
