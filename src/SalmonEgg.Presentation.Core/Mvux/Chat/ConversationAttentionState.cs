using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public enum ConversationAttentionSource
{
    AgentMessage = 0,
    UserEcho = 1,
    ToolCall = 2
}

public sealed record ConversationAttentionSlice(
    string ConversationId,
    bool HasUnread,
    int UnreadVersion,
    DateTime? LastAttentionAtUtc,
    ConversationAttentionSource? LastAttentionSource);

public sealed record ConversationAttentionState(
    IImmutableDictionary<string, ConversationAttentionSlice> Conversations)
{
    public static ConversationAttentionState Empty { get; } =
        new(ImmutableDictionary<string, ConversationAttentionSlice>.Empty.WithComparers(StringComparer.Ordinal));

    public bool TryGetConversation(string conversationId, out ConversationAttentionSlice? slice)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            slice = null;
            return false;
        }

        if (Conversations.TryGetValue(conversationId, out var existing))
        {
            slice = existing;
            return true;
        }

        slice = null;
        return false;
    }
}
