using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
            var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);

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

            var scrubConversationIds = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            var preservedSessionInfoByConversationId = ImmutableDictionary.CreateBuilder<string, ConversationSessionInfoSnapshot?>(
                StringComparer.Ordinal);
            foreach (var duplicateOwner in duplicateOwners)
            {
                scrubConversationIds.Add(duplicateOwner);
                preservedSessionInfoByConversationId[duplicateOwner] = ResolvePreservedSessionInfo(state, duplicateOwner)
                    ?? _workspace.GetConversationSnapshot(duplicateOwner)?.SessionInfo;
            }

            if (replacesRemoteAuthority)
            {
                scrubConversationIds.Add(conversationId);
                preservedSessionInfoByConversationId[conversationId] = ResolvePreservedSessionInfo(state, conversationId)
                    ?? _workspace.GetConversationSnapshot(conversationId)?.SessionInfo;
            }

            var binding = new ConversationBindingSlice(conversationId, remoteSessionId, boundProfileId);
            await _chatStore.Dispatch(new ApplyBindingUpdateAction(
                binding,
                preservedSessionInfoByConversationId.ToImmutable(),
                scrubConversationIds.ToImmutable())).ConfigureAwait(false);
            if (!await AreAuthoritativeBindingsVisibleAsync(binding, duplicateOwners).ConfigureAwait(false))
            {
                return BindingUpdateResult.Error("BindingStoreMismatch");
            }

            foreach (var duplicateOwner in duplicateOwners)
            {
                _workspace.ClearConversationRuntimeContent(duplicateOwner);
                _workspace.UpdateRemoteBinding(duplicateOwner, remoteSessionId: null, boundProfileId: null);
            }

            if (replacesRemoteAuthority)
            {
                _workspace.ClearConversationRuntimeContent(conversationId);
            }

            if (conversationExists)
            {
                _workspace.UpdateRemoteBinding(conversationId, remoteSessionId, boundProfileId);
            }
            if (duplicateOwners.Count > 0 || conversationExists)
            {
                // Binding ownership is recovery metadata; persist before returning success.
                await _workspace.SaveAsync().ConfigureAwait(false);
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

    private async Task<bool> AreAuthoritativeBindingsVisibleAsync(
        ConversationBindingSlice expectedBinding,
        IReadOnlyList<string> clearedDuplicateOwnerIds)
    {
        var currentState = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        if (!MatchesBinding(currentState.ResolveBinding(expectedBinding.ConversationId), expectedBinding))
        {
            return false;
        }

        foreach (var conversationId in clearedDuplicateOwnerIds)
        {
            if (currentState.ResolveBinding(conversationId) is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesBinding(
        ConversationBindingSlice? actualBinding,
        ConversationBindingSlice? expectedBinding)
    {
        if (expectedBinding is null || IsClearedBinding(expectedBinding))
        {
            return actualBinding is null;
        }

        return actualBinding == expectedBinding;
    }

    private static bool IsClearedBinding(ConversationBindingSlice binding)
        => string.IsNullOrWhiteSpace(binding.RemoteSessionId)
            && string.IsNullOrWhiteSpace(binding.ProfileId);

    private async Task<IReadOnlyList<string>> FindDuplicateRemoteSessionOwnersAsync(
        string conversationId,
        string? remoteSessionId)
    {
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            return Array.Empty<string>();
        }

        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
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
