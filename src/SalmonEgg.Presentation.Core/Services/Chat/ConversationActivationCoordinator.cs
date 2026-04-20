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
    private readonly IConversationMutationPipeline _mutationPipeline;
    private readonly ILogger<ConversationActivationCoordinator> _logger;

    public ConversationActivationCoordinator(
        ChatConversationWorkspace conversationWorkspace,
        IConversationBindingCommands bindingCommands,
        IChatStore chatStore,
        IChatConnectionStore chatConnectionStore,
        ILogger<ConversationActivationCoordinator> logger,
        IConversationMutationPipeline? mutationPipeline = null)
    {
        _conversationWorkspace = conversationWorkspace ?? throw new ArgumentNullException(nameof(conversationWorkspace));
        _bindingCommands = bindingCommands ?? throw new ArgumentNullException(nameof(bindingCommands));
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _chatConnectionStore = chatConnectionStore ?? throw new ArgumentNullException(nameof(chatConnectionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mutationPipeline = mutationPipeline ?? new ConversationMutationPipeline();
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
            var snapshot = hydrationMode == ConversationActivationHydrationMode.WorkspaceSnapshot
                ? _conversationWorkspace.GetConversationSnapshot(sessionId)
                : null;
            var shouldHydrateContent = hydrationMode == ConversationActivationHydrationMode.WorkspaceSnapshot
                && ShouldHydrateContent(currentState, sessionId);
            var sessionState = currentState.ResolveSessionStateSlice(sessionId);
            var shouldHydrateSessionState = hydrationMode == ConversationActivationHydrationMode.WorkspaceSnapshot
                && ShouldHydratePrimarySessionState(currentState, sessionId);
            var shouldHydrateAuxiliarySessionState = hydrationMode == ConversationActivationHydrationMode.WorkspaceSnapshot
                && ShouldHydrateAuxiliarySessionState(sessionState, snapshot);
            if (!string.Equals(currentState.HydratedConversationId, sessionId, StringComparison.Ordinal))
            {
                await _chatStore.Dispatch(new SelectConversationAction(sessionId));
            }

            if (shouldHydrateContent || shouldHydrateSessionState || shouldHydrateAuxiliarySessionState)
            {
                if (shouldHydrateContent)
                {
                    await _chatStore.Dispatch(new HydrateConversationAction(
                        sessionId,
                        snapshot?.Transcript.ToImmutableList() ?? ImmutableList<ConversationMessageSnapshot>.Empty,
                        snapshot?.Plan.ToImmutableList() ?? ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                        snapshot?.ShowPlanPanel ?? false,
                        snapshot?.PlanTitle)).ConfigureAwait(false);
                }

                if (shouldHydrateSessionState)
                {
                    await _chatStore.Dispatch(new SetConversationSessionStateAction(
                        sessionId,
                        snapshot?.AvailableModes?.ToImmutableList() ?? ImmutableList<ConversationModeOptionSnapshot>.Empty,
                        snapshot?.SelectedModeId,
                        snapshot?.ConfigOptions?.ToImmutableList() ?? ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                        snapshot?.ShowConfigOptionsPanel ?? false,
                        ShouldHydrateAvailableCommands(sessionState, snapshot)
                            ? snapshot?.AvailableCommands?.ToImmutableList() ?? ImmutableList<ConversationAvailableCommandSnapshot>.Empty
                            : sessionState?.AvailableCommands ?? ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                        ShouldHydrateSessionInfo(sessionState, snapshot)
                            ? snapshot?.SessionInfo
                            : sessionState?.SessionInfo,
                        ShouldHydrateUsage(sessionState, snapshot)
                            ? snapshot?.Usage
                            : sessionState?.Usage)).ConfigureAwait(false);
                }
                else if (shouldHydrateAuxiliarySessionState)
                {
                    await _chatStore.Dispatch(new MergeConversationSessionStateAction(
                        sessionId,
                        AvailableCommands: ShouldHydrateAvailableCommands(sessionState, snapshot)
                            ? snapshot?.AvailableCommands?.ToImmutableList() ?? ImmutableList<ConversationAvailableCommandSnapshot>.Empty
                            : null,
                        SessionInfo: ShouldHydrateSessionInfo(sessionState, snapshot)
                            ? snapshot?.SessionInfo
                            : null,
                        Usage: ShouldHydrateUsage(sessionState, snapshot)
                            ? snapshot?.Usage
                            : null)).ConfigureAwait(false);
                }

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
            ConversationRemovalMode.Archive,
            cancellationToken);

    public Task<ConversationMutationResult> DeleteConversationAsync(
        string conversationId,
        string? activeConversationId,
        CancellationToken cancellationToken = default)
        => RemoveConversationAsync(
            conversationId,
            activeConversationId,
            ConversationRemovalMode.Delete,
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

    private static bool ShouldHydrateContent(ChatState state, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var content = state.ResolveContentSlice(sessionId)
            ?? new ConversationContentSlice(
                ImmutableList<ConversationMessageSnapshot>.Empty,
                ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                false,
                null);
        var runtimeState = state.ResolveRuntimeState(sessionId);
        if (runtimeState?.Phase == ConversationRuntimePhase.Warm)
        {
            return false;
        }

        return !HasProjectedConversationContent(content);
    }

    private static bool ShouldHydratePrimarySessionState(ChatState state, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var sessionState = state.ResolveSessionStateSlice(sessionId)
            ?? new ConversationSessionStateSlice(
                ImmutableList<ConversationModeOptionSnapshot>.Empty,
                null,
                ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                false,
                ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                null,
                null);

        var runtimeState = state.ResolveRuntimeState(sessionId);
        if (runtimeState?.Phase == ConversationRuntimePhase.Warm)
        {
            return false;
        }

        return !HasProjectedPrimarySessionState(sessionState);
    }

    private static bool HasProjectedConversationContent(ConversationContentSlice content)
        => content.Transcript.Count > 0
            || content.PlanEntries.Count > 0
            || content.ShowPlanPanel
            || !string.IsNullOrWhiteSpace(content.PlanTitle);

    private static bool HasProjectedPrimarySessionState(ConversationSessionStateSlice sessionState)
        => sessionState.AvailableModes.Count > 0
            || sessionState.ConfigOptions.Count > 0
            || sessionState.ShowConfigOptionsPanel
            || !string.IsNullOrWhiteSpace(sessionState.SelectedModeId);

    private static bool ShouldHydrateAuxiliarySessionState(
        ConversationSessionStateSlice? sessionState,
        ConversationWorkspaceSnapshot? snapshot)
        => ShouldHydrateAvailableCommands(sessionState, snapshot)
            || ShouldHydrateSessionInfo(sessionState, snapshot)
            || ShouldHydrateUsage(sessionState, snapshot);

    private static bool ShouldHydrateAvailableCommands(
        ConversationSessionStateSlice? sessionState,
        ConversationWorkspaceSnapshot? snapshot)
        => (sessionState?.AvailableCommands.Count ?? 0) == 0
            && (snapshot?.AvailableCommands?.Count ?? 0) > 0;

    private static bool ShouldHydrateSessionInfo(
        ConversationSessionStateSlice? sessionState,
        ConversationWorkspaceSnapshot? snapshot)
        => sessionState?.SessionInfo is null
            && snapshot?.SessionInfo is not null;

    private static bool ShouldHydrateUsage(
        ConversationSessionStateSlice? sessionState,
        ConversationWorkspaceSnapshot? snapshot)
        => sessionState?.Usage is null
            && snapshot?.Usage is not null;

    private async Task<ConversationMutationResult> RemoveConversationAsync(
        string conversationId,
        string? activeConversationId,
        ConversationRemovalMode removalMode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return new ConversationMutationResult(false, false, "ConversationIdMissing");
        }

        return await _mutationPipeline.RunAsync(
            conversationId,
            async token =>
            {
                token.ThrowIfCancellationRequested();
                var transactionContext = await CaptureRemovalTransactionContextAsync(conversationId, activeConversationId).ConfigureAwait(false);

                try
                {
                    return await ExecuteRemovalTransactionAsync(
                        transactionContext,
                        removalMode).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await TryCompensateMutationFailureAsync(
                        transactionContext).ConfigureAwait(false);
                    _logger.LogError(ex, "Conversation mutation failed (ConversationId={ConversationId})", conversationId);
                    return new ConversationMutationResult(false, false, ex.Message);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<RemovalTransactionContext> CaptureRemovalTransactionContextAsync(
        string conversationId,
        string? activeConversationId)
    {
        var currentState = await _chatStore.State ?? ChatState.Empty;
        var hydratedConversationId = currentState.HydratedConversationId;
        var clearsActiveConversation = string.Equals(conversationId, hydratedConversationId, StringComparison.Ordinal)
            || string.Equals(activeConversationId, conversationId, StringComparison.Ordinal);

        return new RemovalTransactionContext(
            conversationId,
            clearsActiveConversation,
            hydratedConversationId,
            currentState.ResolveBinding(conversationId));
    }

    private async Task<ConversationMutationResult> ExecuteRemovalTransactionAsync(
        RemovalTransactionContext context,
        ConversationRemovalMode removalMode)
    {
        var clearBindingResult = await _bindingCommands.ClearBindingAsync(context.ConversationId).ConfigureAwait(false);
        if (clearBindingResult.Status is not BindingUpdateStatus.Success)
        {
            return new ConversationMutationResult(false, false, clearBindingResult.ErrorMessage ?? "BindingClearFailed");
        }

        if (context.ClearsActiveConversation)
        {
            await _chatStore.Dispatch(new SelectConversationAction(null));
        }

        ApplyConversationRemoval(context.ConversationId, removalMode);
        return new ConversationMutationResult(true, context.ClearsActiveConversation, null);
    }

    private void ApplyConversationRemoval(string conversationId, ConversationRemovalMode removalMode)
    {
        switch (removalMode)
        {
            case ConversationRemovalMode.Archive:
                _conversationWorkspace.ArchiveConversation(conversationId);
                break;
            case ConversationRemovalMode.Delete:
                _conversationWorkspace.DeleteConversation(conversationId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(removalMode), removalMode, "Unknown removal mode.");
        }
    }

    private async Task TryCompensateMutationFailureAsync(RemovalTransactionContext context)
    {
        try
        {
            if (context.PreviousBinding is not null)
            {
                await _bindingCommands
                    .UpdateBindingAsync(
                        context.ConversationId,
                        context.PreviousBinding.RemoteSessionId,
                        context.PreviousBinding.ProfileId)
                    .ConfigureAwait(false);
            }

            if (context.ClearsActiveConversation)
            {
                await _chatStore.Dispatch(new SelectConversationAction(context.PreviousHydratedConversationId));
            }
        }
        catch (Exception compensationEx)
        {
            _logger.LogWarning(
                compensationEx,
                "Conversation mutation compensation failed (ConversationId={ConversationId})",
                context.ConversationId);
        }
    }

    private sealed record RemovalTransactionContext(
        string ConversationId,
        bool ClearsActiveConversation,
        string? PreviousHydratedConversationId,
        ConversationBindingSlice? PreviousBinding);

    private enum ConversationRemovalMode
    {
        Archive,
        Delete
    }
}
