using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ConversationActivationCoordinator : IConversationActivationCoordinator
{
    private readonly ChatConversationWorkspace _conversationWorkspace;
    private readonly IConversationBindingCommands _bindingCommands;
    private readonly IChatStore _chatStore;
    private readonly IChatConnectionStore _chatConnectionStore;
    private readonly ILogger<ConversationActivationCoordinator> _logger;

    public ConversationActivationCoordinator(
        ChatConversationWorkspace conversationWorkspace,
        IConversationBindingCommands bindingCommands,
        IChatStore chatStore,
        IChatConnectionStore chatConnectionStore,
        ILogger<ConversationActivationCoordinator> logger)
    {
        _conversationWorkspace = conversationWorkspace ?? throw new ArgumentNullException(nameof(conversationWorkspace));
        _bindingCommands = bindingCommands ?? throw new ArgumentNullException(nameof(bindingCommands));
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _chatConnectionStore = chatConnectionStore ?? throw new ArgumentNullException(nameof(chatConnectionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConversationActivationResult> ActivateSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new ConversationActivationResult(false, null, "SessionIdMissing");
        }

        try
        {
            var switched = await _conversationWorkspace
                .TrySwitchToSessionAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);
            if (!switched)
            {
                return new ConversationActivationResult(false, sessionId, "WorkspaceSwitchRejected");
            }

            var currentState = await _chatStore.State ?? ChatState.Empty;
            var shouldHydrate = ShouldHydrate(currentState, sessionId);
            if (!string.Equals(currentState.HydratedConversationId, sessionId, StringComparison.Ordinal))
            {
                await _chatStore.Dispatch(new SelectConversationAction(sessionId));
            }

            if (shouldHydrate)
            {
                var snapshot = _conversationWorkspace.GetConversationSnapshot(sessionId);
                await _chatStore.Dispatch(new HydrateConversationAction(
                    sessionId,
                    snapshot?.Transcript.ToImmutableList() ?? ImmutableList<ConversationMessageSnapshot>.Empty,
                    snapshot?.Plan.ToImmutableList() ?? ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    snapshot?.ShowPlanPanel ?? false,
                    snapshot?.PlanTitle)).ConfigureAwait(false);
            }

            await NormalizeBindingForSelectedProfileAsync(sessionId).ConfigureAwait(false);
            return new ConversationActivationResult(true, sessionId, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversation activation failed (ConversationId={ConversationId})", sessionId);
            return new ConversationActivationResult(false, sessionId, ex.Message);
        }
    }

    public Task<ConversationMutationResult> ArchiveConversationAsync(
        string conversationId,
        string? activeConversationId,
        CancellationToken cancellationToken = default)
        => RemoveConversationAsync(
            conversationId,
            activeConversationId,
            static (workspace, id) => workspace.ArchiveConversation(id),
            cancellationToken);

    public Task<ConversationMutationResult> DeleteConversationAsync(
        string conversationId,
        string? activeConversationId,
        CancellationToken cancellationToken = default)
        => RemoveConversationAsync(
            conversationId,
            activeConversationId,
            static (workspace, id) => workspace.DeleteConversation(id),
            cancellationToken);

    private async Task NormalizeBindingForSelectedProfileAsync(string conversationId)
    {
        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
        var selectedProfileId = connectionState.SelectedProfileId;
        if (string.IsNullOrWhiteSpace(selectedProfileId))
        {
            return;
        }

        var currentState = await _chatStore.State ?? ChatState.Empty;
        if (!string.Equals(currentState.HydratedConversationId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        var binding = currentState.Binding;
        if (binding is null || !string.Equals(binding.ConversationId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        var boundProfileId = binding.ProfileId;
        if (string.IsNullOrWhiteSpace(boundProfileId)
            || string.Equals(boundProfileId, selectedProfileId, StringComparison.Ordinal))
        {
            return;
        }

        var result = await _bindingCommands
            .UpdateBindingAsync(conversationId, remoteSessionId: null, boundProfileId: selectedProfileId)
            .ConfigureAwait(false);
        if (result.Status is not BindingUpdateStatus.Success)
        {
            throw new InvalidOperationException(
                $"Failed to normalize conversation binding ({result.Status}): {result.ErrorMessage ?? "UnknownError"}");
        }
    }

    private static bool ShouldHydrate(ChatState state, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!string.Equals(state.HydratedConversationId, sessionId, StringComparison.Ordinal))
        {
            return true;
        }

        return state.Transcript is null || state.PlanEntries is null;
    }

    private async Task<ConversationMutationResult> RemoveConversationAsync(
        string conversationId,
        string? activeConversationId,
        Action<ChatConversationWorkspace, string> removeConversation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return new ConversationMutationResult(false, false, "ConversationIdMissing");
        }

        var clearsActiveConversation = string.Equals(activeConversationId, conversationId, StringComparison.Ordinal);
        try
        {
            if (clearsActiveConversation)
            {
                await _chatStore.Dispatch(new SelectConversationAction(null));
            }

            removeConversation(_conversationWorkspace, conversationId);
            return new ConversationMutationResult(true, clearsActiveConversation, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversation mutation failed (ConversationId={ConversationId})", conversationId);
            return new ConversationMutationResult(false, false, ex.Message);
        }
    }
}
