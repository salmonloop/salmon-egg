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

        if (!string.IsNullOrWhiteSpace(message.Id))
        {
            return $"msg:{message.Id}";
        }

        var contentType = message.ContentType ?? string.Empty;
        var textContent = message.TextContent ?? string.Empty;
        return $"idx:{projectionIndex}:{contentType}:{textContent}";
    }

    public TranscriptProjectionRestoreProjection Project(
        string conversationId,
        IReadOnlyList<ConversationMessageSnapshot> transcript,
        int firstVisibleIndex,
        double relativeOffsetWithinItem)
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
                ProjectionItemKey: projectionItemKey,
                OffsetHint: relativeOffsetWithinItem),
            ProjectionEpoch: projectionEpoch,
            IsReady: true);
    }
}

public readonly record struct TranscriptProjectionRestoreProjection(
    TranscriptProjectionRestoreToken? Token,
    long ProjectionEpoch,
    bool IsReady);
