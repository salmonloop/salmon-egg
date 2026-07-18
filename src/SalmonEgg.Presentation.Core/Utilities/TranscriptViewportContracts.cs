namespace SalmonEgg.Presentation.Utilities;

/// <summary>
/// Public follow surface for ChatView automation. Maps 1:1 to TranscriptFollowMode
/// (+ Suspended when no active conversation). No restore/settle intermediate states.
/// </summary>
public enum TranscriptViewportState
{
    Suspended = 0,
    Following = 1,
    DetachedByUser = 2,
}

public readonly record struct TranscriptViewportFact(
    bool HasItems,
    bool IsReady,
    bool IsAtBottom,
    bool IsProgrammaticScrollInFlight);

public readonly record struct TranscriptViewportTransition(
    TranscriptViewportState FromState,
    TranscriptViewportState ToState,
    string ConversationId,
    int Generation,
    string EventName,
    string Reason);

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
    TranscriptProjectionRestoreToken? RestoreToken = null);

public readonly record struct TranscriptViewportOrchestratorSnapshot(
    TranscriptViewportState State,
    bool IsAutoFollowAttached,
    bool IsViewportDetached,
    bool HasPendingSettle,
    bool IsProgrammaticScrollInFlight,
    bool AttachToBottomIntentPending,
    bool UserScrollIntentPending,
    bool UserScrollIntentCompleted,
    bool ScrollToEndScheduled,
    int Generation,
    int ScheduledScrollRequestVersion,
    int ActiveScrollGeneration);

public readonly record struct TranscriptScrollRequestToken(int Generation, string ConversationId);

public readonly record struct TranscriptScrollScheduleToken(int Generation, int RequestVersion, string ConversationId);

public readonly record struct TranscriptViewportViewState(
    bool IsViewReady,
    bool IsViewportReady,
    bool HasMessages,
    bool IsAtBottom);

public enum TranscriptViewportControllerActionKind
{
    None = 0,
    ScrollTranscriptToEnd = 1,
    StopProgrammaticScroll = 2,
    AutoFollowAttached = 3,
    AutoFollowDetached = 4,
    RequestRestore = 5,
    ScrollIntoView = 6,
}

public readonly record struct TranscriptViewportControllerAction(
    TranscriptViewportControllerActionKind Kind,
    TranscriptScrollRequestToken ScrollRequestToken = default,
    TranscriptProjectionRestoreToken? RestoreToken = null,
    int Generation = -1,
    string? ItemKey = null);
