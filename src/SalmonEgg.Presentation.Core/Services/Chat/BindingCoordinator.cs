using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class BindingCoordinator : IConversationBindingCommands
{
    private readonly ChatConversationWorkspace _workspace;
    private readonly IChatStore _chatStore;

    public BindingCoordinator(ChatConversationWorkspace workspace, IChatStore chatStore)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
    }

    public async ValueTask<BindingUpdateResult> UpdateBindingAsync(string conversationId, string? remoteSessionId, string? boundProfileId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return BindingUpdateResult.Error("ConversationIdMissing");
        }

        try
        {
            var conversationExists = _workspace
                .GetKnownConversationIds()
                .Contains(conversationId, StringComparer.Ordinal);
            var duplicateOwners = await FindDuplicateRemoteSessionOwnersAsync(conversationId, remoteSessionId).ConfigureAwait(false);
            var state = await _chatStore.State ?? ChatState.Empty;

            foreach (var duplicateOwner in duplicateOwners)
            {
                var preservedSessionInfo = ResolvePreservedSessionInfo(state, duplicateOwner)
                    ?? _workspace.GetConversationSnapshot(duplicateOwner)?.SessionInfo;
                await _chatStore.Dispatch(new ScrubConversationDerivedStateAction(
                    duplicateOwner,
                    preservedSessionInfo)).ConfigureAwait(false);
                var clearedBinding = new ConversationBindingSlice(duplicateOwner, null, null);
                await _chatStore.Dispatch(new SetBindingSliceAction(clearedBinding)).ConfigureAwait(false);
                _workspace.ClearConversationRuntimeContent(duplicateOwner);
                _workspace.UpdateRemoteBinding(duplicateOwner, remoteSessionId: null, boundProfileId: null);
            }

            var existingBinding = state.ResolveBinding(conversationId);
            if (existingBinding is null)
            {
                var workspaceBinding = _workspace.GetRemoteBinding(conversationId);
                if (workspaceBinding is not null)
                {
                    existingBinding = new ConversationBindingSlice(
                        workspaceBinding.ConversationId,
                        workspaceBinding.RemoteSessionId,
                        workspaceBinding.BoundProfileId);
                }
            }

            var replacesRemoteAuthority =
                !string.IsNullOrWhiteSpace(existingBinding?.RemoteSessionId)
                && !string.Equals(existingBinding.RemoteSessionId, remoteSessionId, StringComparison.Ordinal);
            if (replacesRemoteAuthority)
            {
                var preservedSessionInfo = ResolvePreservedSessionInfo(state, conversationId)
                    ?? _workspace.GetConversationSnapshot(conversationId)?.SessionInfo;
                await _chatStore.Dispatch(new ScrubConversationDerivedStateAction(
                    conversationId,
                    preservedSessionInfo)).ConfigureAwait(false);
                _workspace.ClearConversationRuntimeContent(conversationId);
            }

            var binding = new ConversationBindingSlice(conversationId, remoteSessionId, boundProfileId);
            await _chatStore.Dispatch(new SetBindingSliceAction(binding)).ConfigureAwait(false);
            var projectionVisible = await WaitForProjectedBindingAsync(binding).ConfigureAwait(false);
            if (!projectionVisible)
            {
                return BindingUpdateResult.Error("BindingProjectionTimeout");
            }

            if (conversationExists)
            {
                _workspace.UpdateRemoteBinding(conversationId, remoteSessionId, boundProfileId);
            }
            if (duplicateOwners.Count > 0 || conversationExists)
            {
                _workspace.ScheduleSave();
            }
            return BindingUpdateResult.Success();
        }
        catch (Exception ex)
        {
            return BindingUpdateResult.Error(ex.Message);
        }
    }

    public ValueTask<BindingUpdateResult> ClearBindingAsync(string conversationId)
        => UpdateBindingAsync(conversationId, remoteSessionId: null, boundProfileId: null);

    private async Task<bool> WaitForProjectedBindingAsync(
        ConversationBindingSlice expectedBinding,
        int timeoutMilliseconds = 2000,
        int pollDelayMilliseconds = 10)
    {
        var expectsClearedBinding = string.IsNullOrWhiteSpace(expectedBinding.RemoteSessionId)
            && string.IsNullOrWhiteSpace(expectedBinding.ProfileId);
        var timeoutAt = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < timeoutAt)
        {
            var state = await _chatStore.State;
            var currentState = state ?? ChatState.Empty;
            var actualBinding = currentState.ResolveBinding(expectedBinding.ConversationId);
            if ((expectsClearedBinding && actualBinding is null) || actualBinding == expectedBinding)
            {
                return true;
            }

            await Task.Delay(pollDelayMilliseconds).ConfigureAwait(false);
        }

        var finalStateValue = await _chatStore.State;
        var finalState = finalStateValue ?? ChatState.Empty;
        var finalBinding = finalState.ResolveBinding(expectedBinding.ConversationId);
        return (expectsClearedBinding && finalBinding is null) || finalBinding == expectedBinding;
    }

    private async Task<IReadOnlyList<string>> FindDuplicateRemoteSessionOwnersAsync(
        string conversationId,
        string? remoteSessionId)
    {
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            return Array.Empty<string>();
        }

        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        var state = await _chatStore.State ?? ChatState.Empty;
        if (state.Bindings is not null)
        {
            foreach (var binding in state.Bindings)
            {
                if (string.Equals(binding.Key, conversationId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(binding.Value.RemoteSessionId, remoteSessionId, StringComparison.Ordinal))
                {
                    duplicates.Add(binding.Key);
                }
            }
        }

        foreach (var knownConversationId in _workspace.GetKnownConversationIds())
        {
            if (string.Equals(knownConversationId, conversationId, StringComparison.Ordinal))
            {
                continue;
            }

            var workspaceBinding = _workspace.GetRemoteBinding(knownConversationId);
            if (string.Equals(workspaceBinding?.RemoteSessionId, remoteSessionId, StringComparison.Ordinal))
            {
                duplicates.Add(knownConversationId);
            }
        }

        return duplicates.Count == 0 ? Array.Empty<string>() : duplicates.ToArray();
    }

    private static ConversationSessionInfoSnapshot? ResolvePreservedSessionInfo(
        ChatState state,
        string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        if (string.Equals(state.HydratedConversationId, conversationId, StringComparison.Ordinal))
        {
            return ConversationSessionInfoSnapshots.Clone(state.SessionInfo);
        }

        return ConversationSessionInfoSnapshots.Clone(
            state.ResolveSessionStateSlice(conversationId)?.SessionInfo);
    }
}
