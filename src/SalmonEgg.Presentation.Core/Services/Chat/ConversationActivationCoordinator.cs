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

    public Task<ConversationActivationResult> ActivateSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => ActivateSessionAsync(sessionId, ConversationActivationHydrationMode.WorkspaceSnapshot, cancellationToken);

    public async Task<ConversationActivationResult> ActivateSessionAsync(
        string sessionId,
        ConversationActivationHydrationMode hydrationMode,
        CancellationToken cancellationToken = default)
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
            var shouldHydrate = hydrationMode == ConversationActivationHydrationMode.WorkspaceSnapshot
                && ShouldHydrate(currentState, sessionId);
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
                await _chatStore.Dispatch(new SetConversationSessionStateAction(
                    sessionId,
                    snapshot?.AvailableModes?.ToImmutableList() ?? ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    snapshot?.SelectedModeId,
                    snapshot?.ConfigOptions?.ToImmutableList() ?? ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    snapshot?.ShowConfigOptionsPanel ?? false)).ConfigureAwait(false);
                await _chatStore.Dispatch(new SetIsHydratingAction(false)).ConfigureAwait(false);
            }

            await SyncSelectedProfileFromConversationBindingAsync(sessionId, cancellationToken).ConfigureAwait(false);
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

    private async Task SyncSelectedProfileFromConversationBindingAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var currentState = await _chatStore.State ?? ChatState.Empty;
        var boundProfileId = currentState.ResolveBinding(conversationId)?.ProfileId
            ?? _conversationWorkspace.GetRemoteBinding(conversationId)?.BoundProfileId;
        if (string.IsNullOrWhiteSpace(boundProfileId))
        {
            return;
        }

        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
        if (string.Equals(connectionState.SelectedProfileId, boundProfileId, StringComparison.Ordinal))
        {
            return;
        }

        await _chatConnectionStore
            .Dispatch(new SetSelectedProfileAction(boundProfileId))
            .ConfigureAwait(false);
    }

    private static bool ShouldHydrate(ChatState state, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var content = state.ResolveContentSlice(sessionId)
            ?? new ConversationContentSlice(
                ImmutableList<ConversationMessageSnapshot>.Empty,
                ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                false,
                null);
        var sessionState = state.ResolveSessionStateSlice(sessionId)
            ?? new ConversationSessionStateSlice(
                ImmutableList<ConversationModeOptionSnapshot>.Empty,
                null,
                ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                false);

        var runtimeState = state.ResolveRuntimeState(sessionId);
        if (runtimeState?.Phase == ConversationRuntimePhase.Warm)
        {
            return false;
        }

        return !HasProjectedConversationData(content, sessionState);
    }

    private static bool HasProjectedConversationData(
        ConversationContentSlice content,
        ConversationSessionStateSlice sessionState)
        => content.Transcript.Count > 0
            || content.PlanEntries.Count > 0
            || content.ShowPlanPanel
            || !string.IsNullOrWhiteSpace(content.PlanTitle)
            || sessionState.AvailableModes.Count > 0
            || sessionState.ConfigOptions.Count > 0
            || sessionState.ShowConfigOptionsPanel
            || !string.IsNullOrWhiteSpace(sessionState.SelectedModeId);

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
            removeConversation(_conversationWorkspace, conversationId);
            if (clearsActiveConversation)
            {
                await _chatStore.Dispatch(new SelectConversationAction(null));
            }
            return new ConversationMutationResult(true, clearsActiveConversation, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversation mutation failed (ConversationId={ConversationId})", conversationId);
            return new ConversationMutationResult(false, false, ex.Message);
        }
    }
}
