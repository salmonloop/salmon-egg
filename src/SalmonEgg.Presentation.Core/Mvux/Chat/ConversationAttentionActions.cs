using System;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public abstract record ConversationAttentionAction;

public sealed record MarkConversationUnreadAction(
    string ConversationId,
    ConversationAttentionSource Source,
    DateTime TimestampUtc,
    string? ProfileId = null,
    string? RemoteSessionId = null,
    ConversationMessageSnapshot? Content = null,
    string? ConnectionInstanceId = null) : ConversationAttentionAction;

public sealed record ClearConversationUnreadAction(
    string ConversationId,
    int ObservedVersion,
    string? ProfileId = null,
    string? RemoteSessionId = null,
    ConversationMessageSnapshot? Content = null,
    string? ConnectionInstanceId = null) : ConversationAttentionAction;

public sealed record ReanchorConversationUnreadAction(
    string ConversationId,
    int ObservedVersion,
    string? ProfileId,
    string? RemoteSessionId,
    ConversationMessageSnapshot Content,
    string? ConnectionInstanceId) : ConversationAttentionAction;

public sealed record RemoveConversationAttentionAction(string ConversationId) : ConversationAttentionAction;

public sealed record ReconcileConversationAttentionBindingAction(
    string ConversationId,
    string? ProfileId,
    string? RemoteSessionId,
    int ObservedVersion,
    string? ObservedProfileId,
    string? ObservedRemoteSessionId) : ConversationAttentionAction;

public sealed record DetachConversationAttentionContentAction(
    string? ProfileId,
    string? ConnectionInstanceId,
    string? ConversationId = null,
    int? ObservedVersion = null) : ConversationAttentionAction;
