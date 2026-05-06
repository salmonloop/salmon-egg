namespace SalmonEgg.Presentation.Utilities;

public enum TranscriptViewportState
{
    Idle = 0,
    Settling = 1,
    Following = 2,
    DetachedByUser = 3,
    Suspended = 4,
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

public abstract record TranscriptViewportEvent(string ConversationId, int Generation)
{
    public sealed record SessionActivated(string ConversationId, int Generation)
        : TranscriptViewportEvent(ConversationId, Generation);

    public sealed record ConversationContextInvalidated(string ConversationId, int Generation)
        : TranscriptViewportEvent(ConversationId, Generation);

    public sealed record UserIntentScroll(string ConversationId, int Generation)
        : TranscriptViewportEvent(ConversationId, Generation);

    public sealed record TranscriptAppended(string ConversationId, int Generation, int AddedCount)
        : TranscriptViewportEvent(ConversationId, Generation);

    public sealed record ViewportFactChanged(
        string ConversationId,
        int Generation,
        TranscriptViewportFact Fact)
        : TranscriptViewportEvent(ConversationId, Generation);
}

public enum TranscriptViewportCommandKind
{
    None = 0,
    IssueScrollToBottom = 1,
    StopProgrammaticScroll = 2,
    MarkAutoFollowAttached = 3,
    MarkAutoFollowDetached = 4,
}

public readonly record struct TranscriptViewportCommand(
    TranscriptViewportCommandKind Kind,
    string ConversationId,
    int Generation,
    string? Reason = null,
    TranscriptViewportTransition? Transition = null);
