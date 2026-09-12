using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    public event Func<Task>? ReplyReadObservationRequested;

    private Task RequestVisibleReplyObservationAsync()
        => PostToUiAsync(async () =>
        {
            if (_disposed || ReplyReadObservationRequested is not { } observers) return;
            foreach (Func<Task> observe in observers.GetInvocationList())
            {
                try
                {
                    await observe().ConfigureAwait(true);
                }
                catch (Exception error) when (error is not OperationCanceledException and not AcpException)
                {
                    // A failed native visibility observation cannot change the protocol's turn result.
                    Logger.LogWarning(error, "Could not confirm reply visibility for this view.");
                }
            }
        });

    public async Task<bool> AcknowledgeVisibleReplyAsync(
        string conversationId,
        ConversationMessageSnapshot content,
        Func<bool>? isStillVisible = null)
    {
        if (_disposed || _conversationAttentionStore is null) return false;
        var activationVersion = _conversationActivationOrchestrator.CurrentActivationVersion;
        var acknowledged = false;
        await PostToUiAsync(async () =>
        {
            if (!CanAcknowledgeReply(conversationId, activationVersion, isStillVisible)) return;
            var attention = await _conversationAttentionStore.GetCurrentStateAsync().ConfigureAwait(true);
            if (!attention.TryGetConversation(conversationId, out var pending)
                || pending is not { HasUnread: true } || !ReferenceEquals(pending.Content, content)) return;

            var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(true);
            var binding = state.ResolveBinding(conversationId);
            if (!string.Equals(state.HydratedConversationId, conversationId, StringComparison.Ordinal)
                || binding is null || binding.ProfileId != pending.ProfileId || binding.RemoteSessionId != pending.RemoteSessionId
                || state.ResolveContentSlice(conversationId)?.Transcript.Contains(content) != true) return;
            if (pending.ContentConnectionInstanceId is { } connectionId
                && state.ResolveRuntimeState(conversationId)?.ConnectionInstanceId != connectionId
                && state.ResolveTurn(conversationId)?.ConnectionInstanceId != connectionId) return;
            if (!CanAcknowledgeReply(conversationId, activationVersion, isStillVisible)) return;

            await _conversationAttentionStore.Dispatch(new ClearConversationUnreadAction(
                conversationId, pending.UnreadVersion, pending.ProfileId, pending.RemoteSessionId,
                content, pending.ContentConnectionInstanceId)).ConfigureAwait(true);
            var cleared = await _conversationAttentionStore.GetCurrentStateAsync().ConfigureAwait(true);
            acknowledged = cleared.TryGetConversation(conversationId, out var result)
                && result is { HasUnread: false } && result.UnreadVersion == pending.UnreadVersion
                && ReferenceEquals(result.Content, content);
        }).ConfigureAwait(false);
        return acknowledged;
    }

    private bool CanAcknowledgeReply(string conversationId, long activationVersion, Func<bool>? isStillVisible)
        => !_disposed && !IsActivationOverlayVisible
            && string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal)
            && _conversationActivationOrchestrator.IsLatestActivationVersion(activationVersion)
            && isStillVisible?.Invoke() != false;

    private async Task RecordLiveReplyAsync(string conversationId, AcpSessionEventSource source, bool isReplay = false)
    {
        if (isReplay || _conversationAttentionStore is null || !IsCurrentEventSource(source)) return;
        var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        var turn = state.ResolveTurn(conversationId);
        // v1 replay has no per-message live/replay bit. Only a current prompt establishes live
        // work; hydration alone cannot create an unread reply from historical chunks.
        if (turn is null || turn.ConnectionInstanceId != source.ConnectionInstanceId
            || state.ResolveRuntimeState(conversationId)?.Phase == ConversationRuntimePhase.RemoteHydrating
            || turn.Phase is ChatTurnPhase.Completed or ChatTurnPhase.Cancelled or ChatTurnPhase.Failed) return;
        var content = state.ResolveContentSlice(conversationId)?.Transcript.LastOrDefault(message => !message.IsOutgoing
            && message.ContentType is "text" or "image" or "audio" or "resource" or "resource_link");
        if (content is null) return;

        await _conversationAttentionStore.Dispatch(new MarkConversationUnreadAction(
            conversationId, ConversationAttentionSource.AgentMessage, DateTime.UtcNow,
            source.ProfileId, state.ResolveBinding(conversationId)?.RemoteSessionId, content,
            source.ConnectionInstanceId)).ConfigureAwait(false);
        await RequestVisibleReplyObservationAsync().ConfigureAwait(false);
    }

    private async Task<int?> CaptureUnreadRecoveryVersionAsync(string conversationId, AcpSessionEventSource source)
    {
        if (_conversationAttentionStore is null) return null;
        var state = await _conversationAttentionStore.GetCurrentStateAsync().ConfigureAwait(false);
        return state.TryGetConversation(conversationId, out var pending)
            && pending is { HasUnread: true } && pending.ProfileId == source.ProfileId
            ? pending.UnreadVersion : null;
    }

    private async Task ReanchorUnreadAfterRecoveryAsync(string conversationId, AcpSessionEventSource source, int? observedVersion)
    {
        if (_conversationAttentionStore is null || observedVersion is null || !IsCurrentEventSource(source)) return;
        var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        var binding = state.ResolveBinding(conversationId);
        var content = state.ResolveContentSlice(conversationId)?.Transcript.LastOrDefault(message => !message.IsOutgoing
            && message.ContentType is "text" or "image" or "audio" or "resource" or "resource_link");
        if (binding is null || binding.ProfileId != source.ProfileId || content is null) return;
        await _conversationAttentionStore.Dispatch(new ReanchorConversationUnreadAction(
            conversationId, observedVersion.Value, binding.ProfileId, binding.RemoteSessionId,
            content, source.ConnectionInstanceId)).ConfigureAwait(false);
        await RequestVisibleReplyObservationAsync().ConfigureAwait(false);
    }
}
