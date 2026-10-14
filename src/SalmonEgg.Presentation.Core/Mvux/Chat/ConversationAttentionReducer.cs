using System;
using System.Collections.Immutable;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public static class ConversationAttentionReducer
{
    public static ConversationAttentionState Reduce(
        ConversationAttentionState? state,
        ConversationAttentionAction action)
    {
        var current = state ?? ConversationAttentionState.Empty;

        switch (action)
        {
            case MarkConversationUnreadAction mark when !string.IsNullOrWhiteSpace(mark.ConversationId):
                {
                    var next = current.Conversations;
                    next.TryGetValue(mark.ConversationId, out var existing);
                    if (existing is not null && mark.Content is not null && ReferenceEquals(mark.Content, existing.Content)
                        && mark.ProfileId == existing.ProfileId && mark.RemoteSessionId == existing.RemoteSessionId
                        && mark.ConnectionInstanceId == existing.ContentConnectionInstanceId)
                    {
                        return current;
                    }

                    return new ConversationAttentionState(next.SetItem(
                        mark.ConversationId,
                        new ConversationAttentionSlice(
                            mark.ConversationId,
                            HasUnread: true,
                            UnreadVersion: checked((existing?.UnreadVersion ?? 0) + 1),
                            LastAttentionAtUtc: mark.TimestampUtc,
                            LastAttentionSource: mark.Source,
                            ProfileId: mark.ProfileId,
                            RemoteSessionId: mark.RemoteSessionId,
                            Content: mark.Content,
                            ContentConnectionInstanceId: mark.ConnectionInstanceId)));
                }

            case ClearConversationUnreadAction clear
                when !string.IsNullOrWhiteSpace(clear.ConversationId)
                && current.Conversations.TryGetValue(clear.ConversationId, out var unread)
                && unread.HasUnread
                && clear.ObservedVersion == unread.UnreadVersion
                    && string.Equals(clear.ProfileId, unread.ProfileId, StringComparison.Ordinal)
                    && string.Equals(clear.RemoteSessionId, unread.RemoteSessionId, StringComparison.Ordinal)
                    && string.Equals(clear.ConnectionInstanceId, unread.ContentConnectionInstanceId, StringComparison.Ordinal)
                    && ReferenceEquals(clear.Content, unread.Content):
                return new ConversationAttentionState(current.Conversations.SetItem(clear.ConversationId, unread with { HasUnread = false }));

            case ReanchorConversationUnreadAction reanchor
                when current.Conversations.TryGetValue(reanchor.ConversationId, out var pending)
                    && pending.HasUnread && reanchor.ObservedVersion == pending.UnreadVersion
                    && string.Equals(reanchor.ProfileId, pending.ProfileId, StringComparison.Ordinal)
                    && string.Equals(reanchor.RemoteSessionId, pending.RemoteSessionId, StringComparison.Ordinal):
                return new ConversationAttentionState(current.Conversations.SetItem(reanchor.ConversationId,
                    pending with { Content = reanchor.Content, ContentConnectionInstanceId = reanchor.ConnectionInstanceId }));

            case RemoveConversationAttentionAction remove
                when !string.IsNullOrWhiteSpace(remove.ConversationId)
                && current.Conversations.ContainsKey(remove.ConversationId):
                return new ConversationAttentionState(current.Conversations.Remove(remove.ConversationId));

            case ReconcileConversationAttentionBindingAction binding
                when current.Conversations.TryGetValue(binding.ConversationId, out var boundAttention)
                    && boundAttention.UnreadVersion == binding.ObservedVersion
                    && string.Equals(boundAttention.ProfileId, binding.ObservedProfileId, StringComparison.Ordinal)
                    && string.Equals(boundAttention.RemoteSessionId, binding.ObservedRemoteSessionId, StringComparison.Ordinal)
                    && (!string.Equals(boundAttention.ProfileId, binding.ProfileId, StringComparison.Ordinal)
                        || !string.Equals(boundAttention.RemoteSessionId, binding.RemoteSessionId, StringComparison.Ordinal)):
                return new ConversationAttentionState(current.Conversations.Remove(binding.ConversationId));

            case DetachConversationAttentionContentAction detach:
                var conversations = current.Conversations;
                foreach (var entry in current.Conversations)
                {
                    if (entry.Value.ProfileId == detach.ProfileId
                        && entry.Value.ContentConnectionInstanceId == detach.ConnectionInstanceId
                        && (detach.ConversationId is null || entry.Key == detach.ConversationId)
                        && (!detach.ObservedVersion.HasValue || entry.Value.UnreadVersion == detach.ObservedVersion.Value))
                    {
                        conversations = conversations.SetItem(entry.Key, entry.Value with
                        {
                            Content = null,
                            ContentConnectionInstanceId = null
                        });
                    }
                }

                return ReferenceEquals(conversations, current.Conversations) ? current : new(conversations);
        }

        return current;
    }
}
