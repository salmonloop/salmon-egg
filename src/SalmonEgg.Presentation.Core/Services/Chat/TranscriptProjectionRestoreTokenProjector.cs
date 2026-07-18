using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Stable item-key helper kept under the historical type name for call-site churn control
/// inside Presentation.Core. New code should prefer <see cref="TranscriptItemKey"/>.
/// ProjectionEpoch / restore-token projection has been removed entirely.
/// </summary>
public static class TranscriptProjectionRestoreTokenProjector
{
    public static string CreateProjectionItemKey(
        ConversationMessageSnapshot message,
        int projectionIndex)
        => TranscriptItemKey.FromSnapshot(message, projectionIndex);
}
