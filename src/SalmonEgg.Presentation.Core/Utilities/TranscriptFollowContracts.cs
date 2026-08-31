namespace SalmonEgg.Presentation.Utilities;

/// <summary>
/// User scroll intent for a chat transcript. Native ListView owns scroll physics;
/// this enum is the only application-owned follow fact.
/// </summary>
public enum TranscriptFollowMode
{
    /// <summary>No active conversation viewport.</summary>
    Suspended = 0,

    /// <summary>User wants the latest content; system may ScrollToEnd.</summary>
    FollowingBottom = 1,

    /// <summary>User left the bottom; pin to a stable message identity.</summary>
    PinnedToItem = 2,
}

public readonly record struct TranscriptFollowState(
    string? ConversationId,
    int ActivationGeneration,
    TranscriptFollowMode Mode,
    string? PinnedItemKey);

public readonly record struct TranscriptViewportObservation(
    string ConversationId,
    int ActivationGeneration,
    bool HasItems,
    bool IsAtBottom,
    bool ProgrammaticScrollInFlight,
    string? TopVisibleItemKey,
    bool IsPinnedItemVisible = true);

public enum TranscriptScrollRequestKind
{
    None = 0,
    ScrollToEnd = 1,
    ScrollIntoView = 2,
}

public readonly record struct TranscriptScrollRequest(
    TranscriptScrollRequestKind Kind,
    string? ConversationId = null,
    int ActivationGeneration = 0,
    string? ItemKey = null,
    string Reason = "");
