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
                return new ConversationAttentionState(next.SetItem(
                    mark.ConversationId,
                    new ConversationAttentionSlice(
                        mark.ConversationId,
                        HasUnread: true,
                        UnreadVersion: checked((existing?.UnreadVersion ?? 0) + 1),
                        LastAttentionAtUtc: mark.TimestampUtc,
                        LastAttentionSource: mark.Source)));
            }

            case ClearConversationUnreadAction clear
                when !string.IsNullOrWhiteSpace(clear.ConversationId)
                && current.Conversations.TryGetValue(clear.ConversationId, out var unread)
                && unread.HasUnread:
                return new ConversationAttentionState(current.Conversations.SetItem(clear.ConversationId, unread with { HasUnread = false }));

            case RemoveConversationAttentionAction remove
                when !string.IsNullOrWhiteSpace(remove.ConversationId)
                && current.Conversations.ContainsKey(remove.ConversationId):
                return new ConversationAttentionState(current.Conversations.Remove(remove.ConversationId));
        }

        return current;
    }
}
