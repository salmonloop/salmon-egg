using System;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public abstract record ConversationAttentionAction;

public sealed record MarkConversationUnreadAction(
    string ConversationId,
    ConversationAttentionSource Source,
    DateTime TimestampUtc) : ConversationAttentionAction;

public sealed record ClearConversationUnreadAction(string ConversationId) : ConversationAttentionAction;

public sealed record RemoveConversationAttentionAction(string ConversationId) : ConversationAttentionAction;
