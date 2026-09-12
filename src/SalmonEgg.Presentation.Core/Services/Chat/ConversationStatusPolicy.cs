using System;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public enum ConversationStatusGroup
{
    NeedsAttention,
    Working,
    Other
}

public enum ConversationStatusIcon
{
    Conversation,
    Unread,
    Permission,
    Input,
    Error,
    Working
}

public readonly record struct ConversationStatusPresentation(
    ConversationStatusGroup Group,
    ConversationStatusIcon Icon,
    DateTime? ActivityAt);

public static class ConversationStatusPolicy
{
    public static ConversationStatusPresentation Resolve(
        ActiveTurnState? turn,
        ConversationInteractionSummary interaction,
        ConversationAttentionSlice? attention,
        ConversationOperationFailure? operationFailure = null)
    {
        var activityAt = Later(ResolveActivityAt(turn), Later(interaction.ActivityAtUtc, operationFailure?.OccurredAtUtc));
        if (operationFailure is not null || interaction.HasFailure || turn?.Phase == ChatTurnPhase.Failed)
        {
            return new(ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Error, activityAt);
        }

        if (interaction.HasPermissionRequest)
        {
            return new(ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Permission, activityAt);
        }

        if (interaction.HasInputRequest || turn?.Phase == ChatTurnPhase.WaitingForUser)
        {
            return new(ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Input, activityAt);
        }

        if (turn is not null && turn.Phase is not (ChatTurnPhase.Completed or ChatTurnPhase.Cancelled or ChatTurnPhase.Failed))
        {
            return new(ConversationStatusGroup.Working, ConversationStatusIcon.Working, activityAt);
        }

        if (attention is { HasUnread: true })
        {
            return new(ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Unread,
                activityAt ?? attention.LastAttentionAtUtc);
        }

        return new(ConversationStatusGroup.Other, ConversationStatusIcon.Conversation, activityAt);
    }

    private static DateTime? Later(DateTime? left, DateTime? right)
        => left is { } current && (right is null || current >= right.Value) ? left : right;

    private static DateTime? ResolveActivityAt(ActiveTurnState? turn)
        => turn?.Phase is ChatTurnPhase.Completed or ChatTurnPhase.Cancelled or ChatTurnPhase.Failed
            ? turn.LastUpdatedAtUtc
            : turn?.StartedAtUtc;
}
