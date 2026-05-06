namespace SalmonEgg.Presentation.Utilities;

public sealed class TranscriptViewportCoordinator
{
    private string? _conversationId;
    private int _generation;
    private bool _hasObservedDetachedViewportExit;
    private bool _hasPendingAttachedRecovery;
    private TranscriptViewportFact? _lastFact;

    public TranscriptViewportState State { get; private set; } = TranscriptViewportState.Idle;

    public bool IsAutoFollowAttached { get; private set; } = true;

    public TranscriptViewportTransition? LastTransition { get; private set; }

    public TranscriptViewportCommand Handle(TranscriptViewportEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        return evt switch
        {
            TranscriptViewportEvent.SessionActivated activated => HandleSessionActivated(activated),
            _ when !MatchesContext(evt.ConversationId, evt.Generation) => None(evt, "StaleOrMismatchedContext"),
            TranscriptViewportEvent.ConversationContextInvalidated invalidated => HandleContextInvalidated(invalidated),
            TranscriptViewportEvent.UserIntentScroll userIntent => HandleUserIntentScroll(userIntent),
            TranscriptViewportEvent.TranscriptAppended appended => HandleTranscriptAppended(appended),
            TranscriptViewportEvent.ViewportFactChanged factChanged => HandleViewportFactChanged(factChanged),
            _ => None(evt, "UnhandledEvent"),
        };
    }

    private TranscriptViewportCommand HandleSessionActivated(TranscriptViewportEvent.SessionActivated evt)
    {
        _conversationId = evt.ConversationId;
        _generation = evt.Generation;
        _hasObservedDetachedViewportExit = false;
        _hasPendingAttachedRecovery = false;
        _lastFact = null;

        var targetState = string.IsNullOrWhiteSpace(evt.ConversationId)
            ? TranscriptViewportState.Idle
            : TranscriptViewportState.Settling;
        return Transition(evt, targetState, isAutoFollowAttached: true, TranscriptViewportCommandKind.None, "SessionActivated");
    }

    private TranscriptViewportCommand HandleContextInvalidated(TranscriptViewportEvent.ConversationContextInvalidated evt)
    {
        _hasObservedDetachedViewportExit = false;
        _hasPendingAttachedRecovery = false;
        return Transition(
            evt,
            TranscriptViewportState.Suspended,
            isAutoFollowAttached: false,
            TranscriptViewportCommandKind.StopProgrammaticScroll,
            "ContextInvalidated");
    }

    private TranscriptViewportCommand HandleUserIntentScroll(TranscriptViewportEvent.UserIntentScroll evt)
    {
        _hasObservedDetachedViewportExit = false;
        _hasPendingAttachedRecovery = false;
        return Transition(
            evt,
            TranscriptViewportState.DetachedByUser,
            isAutoFollowAttached: false,
            TranscriptViewportCommandKind.MarkAutoFollowDetached,
            "ExplicitUserIntent");
    }

    private TranscriptViewportCommand HandleTranscriptAppended(TranscriptViewportEvent.TranscriptAppended evt)
    {
        if (evt.AddedCount <= 0 || !IsAutoFollowAttached || State == TranscriptViewportState.Suspended)
        {
            return None(evt, "AppendIgnored");
        }

        _hasPendingAttachedRecovery = true;

        if (_lastFact is { HasItems: true, IsReady: true, IsAtBottom: false, IsProgrammaticScrollInFlight: false })
        {
            return Transition(
                evt,
                TranscriptViewportState.Settling,
                isAutoFollowAttached: true,
                TranscriptViewportCommandKind.IssueScrollToBottom,
                "AttachedAppendNeedsRecovery");
        }

        return None(evt, "AppendQueuedForViewportFact");
    }

    private TranscriptViewportCommand HandleViewportFactChanged(TranscriptViewportEvent.ViewportFactChanged evt)
    {
        _lastFact = evt.Fact;

        if (!evt.Fact.HasItems)
        {
            _hasObservedDetachedViewportExit = false;
            _hasPendingAttachedRecovery = false;
            return Transition(evt, TranscriptViewportState.Idle, isAutoFollowAttached: true, TranscriptViewportCommandKind.None, "NoItems");
        }

        if (State == TranscriptViewportState.DetachedByUser)
        {
            return HandleDetachedViewportFactChanged(evt);
        }

        if (evt.Fact.IsReady && evt.Fact.IsAtBottom)
        {
            _hasPendingAttachedRecovery = false;
            return Transition(
                evt,
                TranscriptViewportState.Following,
                isAutoFollowAttached: true,
                TranscriptViewportCommandKind.MarkAutoFollowAttached,
                "BottomConfirmed");
        }

        if (evt.Fact.IsProgrammaticScrollInFlight)
        {
            return None(evt, "ProgrammaticScrollInFlight");
        }

        if (!evt.Fact.IsReady)
        {
            _hasPendingAttachedRecovery = true;
            return Transition(evt, TranscriptViewportState.Settling, isAutoFollowAttached: true, TranscriptViewportCommandKind.None, "ViewportNotReady");
        }

        var reason = _hasPendingAttachedRecovery
            ? "RecoverPendingAttachedBottom"
            : "RecoverBottomWithoutUserDetach";
        _hasPendingAttachedRecovery = true;
        return Transition(
            evt,
            TranscriptViewportState.Settling,
            isAutoFollowAttached: true,
            TranscriptViewportCommandKind.IssueScrollToBottom,
            reason);
    }

    private TranscriptViewportCommand HandleDetachedViewportFactChanged(TranscriptViewportEvent.ViewportFactChanged evt)
    {
        if (evt.Fact.IsProgrammaticScrollInFlight)
        {
            return None(evt, "DetachedProgrammaticScrollIgnored");
        }

        if (!evt.Fact.IsAtBottom)
        {
            _hasObservedDetachedViewportExit = true;
            return None(evt, "DetachedAwayFromBottom");
        }

        if (!_hasObservedDetachedViewportExit)
        {
            return None(evt, "AwaitingDetachedViewportExit");
        }

        _hasObservedDetachedViewportExit = false;
        _hasPendingAttachedRecovery = false;
        return Transition(
            evt,
            TranscriptViewportState.Following,
            isAutoFollowAttached: true,
            TranscriptViewportCommandKind.MarkAutoFollowAttached,
            "UserReturnedToBottom");
    }

    private bool MatchesContext(string conversationId, int generation)
    {
        return !string.IsNullOrWhiteSpace(_conversationId)
            && string.Equals(_conversationId, conversationId, StringComparison.Ordinal)
            && generation == _generation;
    }

    private TranscriptViewportCommand Transition(
        TranscriptViewportEvent evt,
        TranscriptViewportState targetState,
        bool isAutoFollowAttached,
        TranscriptViewportCommandKind commandKind,
        string reason)
    {
        var previousState = State;
        var previousAttached = IsAutoFollowAttached;
        State = targetState;
        IsAutoFollowAttached = isAutoFollowAttached;

        TranscriptViewportTransition? transition = null;
        if (previousState != targetState || previousAttached != isAutoFollowAttached)
        {
            transition = new TranscriptViewportTransition(
                previousState,
                targetState,
                evt.ConversationId,
                evt.Generation,
                evt.GetType().Name,
                reason);
            LastTransition = transition;
        }

        if (commandKind is TranscriptViewportCommandKind.MarkAutoFollowAttached or TranscriptViewportCommandKind.MarkAutoFollowDetached
            && transition is null)
        {
            commandKind = TranscriptViewportCommandKind.None;
        }

        return new TranscriptViewportCommand(commandKind, evt.ConversationId, evt.Generation, reason, transition);
    }

    private static TranscriptViewportCommand None(TranscriptViewportEvent evt, string reason)
    {
        return new TranscriptViewportCommand(TranscriptViewportCommandKind.None, evt.ConversationId, evt.Generation, reason);
    }
}
