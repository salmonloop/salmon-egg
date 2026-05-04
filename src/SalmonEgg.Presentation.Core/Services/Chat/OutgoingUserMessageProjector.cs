using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class OutgoingUserMessageProjector
{
    public UserMessageProjection ResolveAuthoritativeProjection(
        IImmutableList<ConversationMessageSnapshot> transcript,
        UserMessageUpdate userMessageUpdate,
        ActiveTurnState? activeTurn)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(userMessageUpdate);

        var content = userMessageUpdate.Content;
        if (content is null)
        {
            return new UserMessageProjection(null, null);
        }

        var existing = ResolveExistingOutgoingUserMessageSnapshot(
            transcript,
            userMessageUpdate.MessageId,
            content,
            activeTurn);
        var resolvedProtocolMessageId = existing is null
            ? userMessageUpdate.MessageId
            : userMessageUpdate.MessageId ?? existing.ProtocolMessageId;

        return new UserMessageProjection(existing, resolvedProtocolMessageId);
    }

    public ConversationMessageSnapshot? TryReconcilePromptAcknowledgement(
        IImmutableList<ConversationMessageSnapshot> transcript,
        string pendingUserMessageLocalId,
        string? responseUserMessageId)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        if (string.IsNullOrWhiteSpace(pendingUserMessageLocalId)
            || string.IsNullOrWhiteSpace(responseUserMessageId))
        {
            return null;
        }

        var existing = transcript.LastOrDefault(message =>
            message.IsOutgoing
            && string.Equals(message.Id, pendingUserMessageLocalId, StringComparison.Ordinal));
        if (existing is null
            || string.Equals(existing.ProtocolMessageId, responseUserMessageId, StringComparison.Ordinal))
        {
            return null;
        }

        var reconciled = CloneSnapshot(existing);
        reconciled.ProtocolMessageId = responseUserMessageId;
        return reconciled;
    }

    private static ConversationMessageSnapshot? ResolveExistingOutgoingUserMessageSnapshot(
        IImmutableList<ConversationMessageSnapshot> transcript,
        string? protocolMessageId,
        ContentBlock content,
        ActiveTurnState? activeTurn)
    {
        if (!string.IsNullOrWhiteSpace(protocolMessageId))
        {
            var byProtocolMessageId = transcript.LastOrDefault(message =>
                message.IsOutgoing
                && string.Equals(message.ProtocolMessageId, protocolMessageId, StringComparison.Ordinal));
            if (byProtocolMessageId is not null)
            {
                return byProtocolMessageId;
            }

            var pendingOptimisticOutgoingMessage = ResolvePendingOptimisticOutgoingMessage(transcript, activeTurn);
            if (pendingOptimisticOutgoingMessage is not null)
            {
                return pendingOptimisticOutgoingMessage;
            }
        }

        if (!CanReusePendingLocalUserMessage(activeTurn, content))
        {
            return null;
        }

        return transcript.LastOrDefault(message =>
            message.IsOutgoing
            && string.Equals(message.Id, activeTurn!.PendingUserMessageLocalId, StringComparison.Ordinal));
    }

    private static ConversationMessageSnapshot? ResolvePendingOptimisticOutgoingMessage(
        IImmutableList<ConversationMessageSnapshot> transcript,
        ActiveTurnState? activeTurn)
    {
        if (activeTurn is null || string.IsNullOrWhiteSpace(activeTurn.PendingUserMessageLocalId))
        {
            return null;
        }

        return transcript.LastOrDefault(message =>
            message.IsOutgoing
            && string.Equals(message.Id, activeTurn.PendingUserMessageLocalId, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(message.ProtocolMessageId));
    }

    private static bool CanReusePendingLocalUserMessage(ActiveTurnState? activeTurn, ContentBlock content)
    {
        if (activeTurn is null
            || string.IsNullOrWhiteSpace(activeTurn.PendingUserMessageLocalId))
        {
            return false;
        }

        if (IsTerminalTurnPhase(activeTurn.Phase))
        {
            return false;
        }

        if (content is not TextContentBlock textContent)
        {
            return false;
        }

        return string.Equals(
            activeTurn.PendingUserMessageText ?? string.Empty,
            textContent.Text ?? string.Empty,
            StringComparison.Ordinal);
    }

    private static bool IsTerminalTurnPhase(ChatTurnPhase phase)
        => phase is ChatTurnPhase.Completed or ChatTurnPhase.Failed or ChatTurnPhase.Cancelled;

    private static ConversationMessageSnapshot CloneSnapshot(ConversationMessageSnapshot snapshot)
    {
        return new ConversationMessageSnapshot
        {
            Id = snapshot.Id,
            Timestamp = snapshot.Timestamp,
            IsOutgoing = snapshot.IsOutgoing,
            ContentType = snapshot.ContentType,
            Title = snapshot.Title,
            TextContent = snapshot.TextContent,
            ImageData = snapshot.ImageData,
            ImageMimeType = snapshot.ImageMimeType,
            AudioData = snapshot.AudioData,
            AudioMimeType = snapshot.AudioMimeType,
            ProtocolMessageId = snapshot.ProtocolMessageId,
            ToolCallId = snapshot.ToolCallId,
            ToolCallKind = snapshot.ToolCallKind,
            ToolCallStatus = snapshot.ToolCallStatus,
            ToolCallJson = snapshot.ToolCallJson,
            ToolCallContent = snapshot.ToolCallContent is null ? null : new List<ToolCallContent>(snapshot.ToolCallContent),
            PlanEntry = snapshot.PlanEntry is null
                ? null
                : new ConversationPlanEntrySnapshot
                {
                    Content = snapshot.PlanEntry.Content,
                    Status = snapshot.PlanEntry.Status,
                    Priority = snapshot.PlanEntry.Priority
                },
            ModeId = snapshot.ModeId
        };
    }
}

public sealed record UserMessageProjection(
    ConversationMessageSnapshot? ExistingSnapshot,
    string? ProtocolMessageId);
