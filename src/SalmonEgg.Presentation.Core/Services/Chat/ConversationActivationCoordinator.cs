using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ConversationActivationCoordinator : IConversationActivationCoordinator
{
    private readonly ChatConversationWorkspace _conversationWorkspace;
    private readonly IConversationBindingCommands _bindingCommands;
    private readonly IChatStore _chatStore;
    private readonly AppPreferencesViewModel _preferences;
    private readonly ILogger<ConversationActivationCoordinator> _logger;

    public ConversationActivationCoordinator(
        ChatConversationWorkspace conversationWorkspace,
        IConversationBindingCommands bindingCommands,
        IChatStore chatStore,
        AppPreferencesViewModel preferences,
        ILogger<ConversationActivationCoordinator> logger)
    {
        _conversationWorkspace = conversationWorkspace ?? throw new ArgumentNullException(nameof(conversationWorkspace));
        _bindingCommands = bindingCommands ?? throw new ArgumentNullException(nameof(bindingCommands));
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
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
            var shouldHydrate = ShouldHydrate(currentState);
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

            if (shouldHydrate)
            {
                await UpdateBindingSliceFromWorkspaceAsync(sessionId).ConfigureAwait(false);
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
        var selectedProfileId = _preferences.LastSelectedServerId;
        if (string.IsNullOrWhiteSpace(selectedProfileId))
        {
            return;
        }

        var currentState = await _chatStore.State ?? ChatState.Empty;
        if (!string.Equals(currentState.HydratedConversationId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        var binding = _conversationWorkspace.GetRemoteBinding(conversationId);
        var boundProfileId = binding?.BoundProfileId;
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

    private async Task UpdateBindingSliceFromWorkspaceAsync(string conversationId)
    {
        var binding = _conversationWorkspace.GetRemoteBinding(conversationId);
        var slice = binding is null
            ? null
            : new ConversationBindingSlice(binding.ConversationId, binding.RemoteSessionId, binding.BoundProfileId);
        await _chatStore.Dispatch(new SetBindingSliceAction(slice)).ConfigureAwait(false);
    }

    private static bool ShouldHydrate(ChatState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Generation != 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(state.SelectedConversationId)
            || !string.IsNullOrWhiteSpace(state.HydratedConversationId))
        {
            return false;
        }

        if (!IsBindingEmpty(state.Binding))
        {
            return false;
        }

        if (state.Transcript is { Count: > 0 } || state.PlanEntries is { Count: > 0 })
        {
            return false;
        }

        return true;
    }

    private static bool IsBindingEmpty(ConversationBindingSlice? binding)
    {
        if (binding is null)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(binding.ConversationId)
            && string.IsNullOrWhiteSpace(binding.RemoteSessionId)
            && string.IsNullOrWhiteSpace(binding.ProfileId);
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
