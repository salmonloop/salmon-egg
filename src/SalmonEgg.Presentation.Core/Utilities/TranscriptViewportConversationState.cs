namespace SalmonEgg.Presentation.Utilities;

public enum TranscriptViewportAnchorKind
{
    FirstVisibleItem = 0,
    PrimaryReadingItem = 1,
}

public readonly record struct TranscriptViewportAnchor(
    string MessageId,
    TranscriptViewportAnchorKind Kind,
    double RelativeOffsetWithinAnchor,
    int TranscriptVersion,
    int DistanceFromEnd = 0,
    string? ContentSignature = null);

public readonly record struct TranscriptViewportConversationState(
    TranscriptViewportState Mode,
    TranscriptViewportAnchor? Anchor,
    bool LastKnownBottomState,
    int LastActivationGeneration,
    bool RestorePending,
    TranscriptProjectionRestoreToken? RestoreToken = null,
    long? PendingProjectionEpoch = null);
