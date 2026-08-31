namespace SalmonEgg.Presentation.Utilities;

/// <summary>
/// Public follow surface for ChatView automation. Maps 1:1 to TranscriptFollowMode
/// (+ Suspended when no active conversation). Native ListView owns scroll execution.
/// </summary>
public enum TranscriptViewportState
{
    Suspended = 0,
    Following = 1,
    DetachedByUser = 2,
}

/// <summary>Stable item pin only — no ProjectionEpoch content clock.</summary>
public readonly record struct TranscriptProjectionRestoreToken(
    string ConversationId,
    string ProjectionItemKey);

public enum TranscriptViewportActivationKind
{
    ColdEnter = 0,
    WarmReturn = 1,
    OverlayResume = 2,
}

public readonly record struct TranscriptViewportConversationState(
    TranscriptViewportState Mode,
    TranscriptProjectionRestoreToken? RestoreToken = null);

public readonly record struct TranscriptScrollRequestToken(
    int ActivationGeneration,
    long RequestGeneration,
    string ConversationId);

public readonly record struct TranscriptViewportViewState(
    bool HasMessages,
    bool IsAtBottom);

public enum TranscriptViewportControllerActionKind
{
    ScrollTranscriptToEnd = 1,
    RequestRestore = 5,
    ScrollIntoView = 6,
}

public readonly record struct TranscriptViewportControllerAction(
    TranscriptViewportControllerActionKind Kind,
    TranscriptScrollRequestToken ScrollRequestToken = default,
    TranscriptProjectionRestoreToken? RestoreToken = null,
    int Generation = -1,
    string? ItemKey = null);
