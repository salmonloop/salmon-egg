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

        // Agent errors: operation failure or turn failed without recoverable input
        if (operationFailure is not null || turn?.Phase == ChatTurnPhase.Failed)
        {
            return new(ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Error, activityAt);
        }

        // A request the user can no longer answer normally: an AskUser/elicitation request that
        // errored, or a permission request left with cancel as its only option. HasFailure implies
        // HasInputRequest for the errored-request cases, so this has to precede both checks below or
        // those rows would show the input glyph instead of the warning the failure warrants.
        if (interaction.HasFailure)
        {
            return new(ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Error, activityAt);
        }

        if (interaction.HasPermissionRequest)
        {
            return new(ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Permission, activityAt);
        }

        // A live AskUser/elicitation request, or a turn parked on the user.
        if (interaction.HasInputRequest || turn?.Phase == ChatTurnPhase.WaitingForUser)
        {
            return new(ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Input, activityAt);
        }

        // Active working states (excludes Completed, Cancelled, Failed)
        if (turn is not null && turn.Phase is not (ChatTurnPhase.Completed or ChatTurnPhase.Cancelled))
        {
            return new(ConversationStatusGroup.Working, ConversationStatusIcon.Working, activityAt);
        }

        // Unread messages require attention even after turn completion/cancellation
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
