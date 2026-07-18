using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Stable transcript item identity for viewport pin / ScrollIntoView.
/// Authority ladder: application Id → protocol message Id → tool-call Id.
/// Mutable body text is never part of identity. Index-only keys are not restorable.
/// </summary>
public static class TranscriptItemKey
{
    public static string? TryFromSnapshot(ConversationMessageSnapshot message)
    {
        ArgumentNullException.ThrowIfNull(message);

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

        return null;
    }

    public static string FromSnapshot(ConversationMessageSnapshot message, int projectionIndex)
    {
        var key = TryFromSnapshot(message);
        if (key is not null)
        {
            return key;
        }

        // Non-restorable diagnostic fallback only — never used as a pin target.
        var contentType = message.ContentType ?? string.Empty;
        var direction = message.IsOutgoing ? "out" : "in";
        return $"ephemeral:{projectionIndex}:{contentType}:{direction}";
    }

    public static bool IsRestorable(string? itemKey)
        => !string.IsNullOrWhiteSpace(itemKey)
           && !itemKey.StartsWith("ephemeral:", StringComparison.Ordinal);
}
