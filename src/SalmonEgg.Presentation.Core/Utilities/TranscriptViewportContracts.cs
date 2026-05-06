namespace SalmonEgg.Presentation.Utilities;

public enum TranscriptViewportState
{
    Idle = 0,
    Settling = 1,
    Following = 2,
    DetachedByUser = 3,
    Suspended = 4,
    DetachedPendingRestore = 5,
    DetachedRestoring = 6,
    BootstrapSettling = 7,
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

public readonly record struct TranscriptProjectionRestoreToken(
    string ConversationId,
    long ProjectionEpoch,
    string ProjectionItemKey,
    double OffsetHint);

public enum TranscriptViewportActivationKind
{
    ColdEnter = 0,
    WarmReturn = 1,
    OverlayResume = 2,
}

public abstract record TranscriptViewportEvent(string ConversationId, int Generation)
{
    public sealed record SessionActivated(
        string ConversationId,
        int Generation,
        TranscriptViewportActivationKind ActivationKind = TranscriptViewportActivationKind.ColdEnter)
        : TranscriptViewportEvent(ConversationId, Generation);

    public sealed record ConversationContextInvalidated(string ConversationId, int Generation)
        : TranscriptViewportEvent(ConversationId, Generation);

    public sealed record UserDetached : TranscriptViewportEvent
    {
        public UserDetached(
            string ConversationId,
            int Generation,
            TranscriptProjectionRestoreToken restoreToken)
            : base(ConversationId, Generation)
        {
            RestoreToken = restoreToken;
        }

        public UserDetached(
            string ConversationId,
            int Generation,
            TranscriptViewportAnchor anchor)
            : base(ConversationId, Generation)
        {
            Anchor = anchor;
        }

        public TranscriptProjectionRestoreToken? RestoreToken { get; }

        public TranscriptViewportAnchor? Anchor { get; }
    }

    public sealed record UserAttached(string ConversationId, int Generation)
        : TranscriptViewportEvent(ConversationId, Generation);

    public sealed record UserIntentScroll(string ConversationId, int Generation)
        : TranscriptViewportEvent(ConversationId, Generation);

    public sealed record TranscriptAppended(string ConversationId, int Generation, int AddedCount)
        : TranscriptViewportEvent(ConversationId, Generation);

    public record ViewportObserved(
        string ConversationId,
        int Generation,
        TranscriptViewportFact Fact)
        : TranscriptViewportEvent(ConversationId, Generation);

    // Compatibility bridge for the existing page-local coordinator call sites.
    // Task 1 introduces the conversation-scoped name without forcing coordinator edits yet.
    public sealed record ViewportFactChanged(
        string ConversationId,
        int Generation,
        TranscriptViewportFact Fact)
        : ViewportObserved(ConversationId, Generation, Fact);

    public sealed record ProjectionReady(
        string ConversationId,
        int Generation,
        long ProjectionEpoch)
        : TranscriptViewportEvent(ConversationId, Generation);

    public sealed record RestoreConfirmed(
        string ConversationId,
        int Generation,
        TranscriptProjectionRestoreToken RestoreToken)
        : TranscriptViewportEvent(ConversationId, Generation);

    public sealed record RestoreUnavailable(
        string ConversationId,
        int Generation,
        string Reason)
        : TranscriptViewportEvent(ConversationId, Generation);

    public sealed record RestoreAbandoned(
        string ConversationId,
        int Generation,
        string Reason)
        : TranscriptViewportEvent(ConversationId, Generation);
}

public enum TranscriptViewportCommandKind
{
    None = 0,
    IssueScrollToBottom = 1,
    StopProgrammaticScroll = 2,
    MarkAutoFollowAttached = 3,
    MarkAutoFollowDetached = 4,
    RestoreAnchor = 5,
    RequestRestore = 6,
}

public readonly record struct TranscriptViewportCommand(
    TranscriptViewportCommandKind Kind,
    string ConversationId,
    int Generation,
    string? Reason = null,
    TranscriptViewportTransition? Transition = null,
    TranscriptViewportAnchor? Anchor = null,
    TranscriptProjectionRestoreToken? RestoreToken = null);
