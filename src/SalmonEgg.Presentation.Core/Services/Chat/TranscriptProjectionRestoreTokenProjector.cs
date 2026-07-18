using System.Collections.Generic;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Utilities;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class TranscriptProjectionRestoreTokenProjector
{
    public static string CreateProjectionItemKey(
        ConversationMessageSnapshot message,
        int projectionIndex)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Identity ladder (first principles):
        // 1) Durable application Id
        // 2) Protocol message Id when present
        // 3) Tool-call Id for tool rows
        // 4) Epoch-scoped index + immutable template shape (never mutable body text)
        if (!string.IsNullOrWhiteSpace(message.Id))
        {
            return $"msg:{message.Id}";
        }

        if (!string.IsNullOrWhiteSpace(message.ProtocolMessageId))
        {
            return $"proto:{message.ProtocolMessageId}";
        }

        if (!string.IsNullOrWhiteSpace(message.ToolCallId))
        {
            return $"tool:{message.ToolCallId}";
        }

        var contentType = message.ContentType ?? string.Empty;
        var direction = message.IsOutgoing ? "out" : "in";
        return $"idx:{projectionIndex}:{contentType}:{direction}";
    }

    public TranscriptProjectionRestoreProjection Project(
        string conversationId,
        IReadOnlyList<ConversationMessageSnapshot> transcript,
        int firstVisibleIndex)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var projectionEpoch = transcript.Count;
        if (string.IsNullOrWhiteSpace(conversationId)
            || transcript.Count == 0
            || firstVisibleIndex < 0
            || firstVisibleIndex >= transcript.Count)
        {
            return new TranscriptProjectionRestoreProjection(
                Token: null,
                ProjectionEpoch: projectionEpoch,
                IsReady: false);
        }

        var anchor = transcript[firstVisibleIndex];
        var projectionItemKey = CreateProjectionItemKey(anchor, firstVisibleIndex);

        return new TranscriptProjectionRestoreProjection(
            Token: new TranscriptProjectionRestoreToken(
                conversationId,
                ProjectionEpoch: projectionEpoch,
                ProjectionItemKey: projectionItemKey),
            ProjectionEpoch: projectionEpoch,
            IsReady: true);
    }
}

public readonly record struct TranscriptProjectionRestoreProjection(
    TranscriptProjectionRestoreToken? Token,
    long ProjectionEpoch,
    bool IsReady);
