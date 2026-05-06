namespace SalmonEgg.Presentation.Utilities;

public sealed class TranscriptViewportCoordinator
{
    private readonly Dictionary<string, TranscriptViewportConversationState> _conversationStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TranscriptViewportObservedFact> _lastFacts = new(StringComparer.Ordinal);

    private string? _activeConversationId;
    private int _activeGeneration;

    public TranscriptViewportState State { get; private set; } = TranscriptViewportState.Idle;

    public bool IsAutoFollowAttached { get; private set; } = true;

    public TranscriptViewportTransition? LastTransition { get; private set; }

    public TranscriptViewportCommand Handle(TranscriptViewportEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        return evt switch
        {
            TranscriptViewportEvent.SessionActivated activated => HandleSessionActivated(activated),
            _ when !MatchesActiveContext(evt.ConversationId, evt.Generation) => None(evt, "StaleOrMismatchedContext"),
            TranscriptViewportEvent.ConversationContextInvalidated invalidated => HandleContextInvalidated(invalidated),
            TranscriptViewportEvent.UserAttached attached => HandleUserAttached(attached),
            TranscriptViewportEvent.UserDetached detached => HandleUserDetached(detached),
            TranscriptViewportEvent.UserIntentScroll userIntent => HandleUserIntentScroll(userIntent),
            TranscriptViewportEvent.TranscriptAppended appended => HandleTranscriptAppended(appended),
            TranscriptViewportEvent.ViewportObserved observed => HandleViewportObserved(observed),
            _ => None(evt, "UnhandledEvent"),
        };
    }

    private TranscriptViewportCommand HandleSessionActivated(TranscriptViewportEvent.SessionActivated evt)
    {
        _activeConversationId = evt.ConversationId;
        _activeGeneration = evt.Generation;

        if (string.IsNullOrWhiteSpace(evt.ConversationId))
        {
            return Transition(
                evt,
                new TranscriptViewportConversationState(
                    TranscriptViewportState.Idle,
                    Anchor: null,
                    LastKnownBottomState: true,
                    LastActivationGeneration: evt.Generation,
                    RestorePending: false),
                isAutoFollowAttached: true,
                TranscriptViewportCommandKind.None,
                "SessionActivatedEmpty");
        }

        var existing = GetConversationStateOrDefault(evt.ConversationId, evt.Generation);

        if (existing.Mode == TranscriptViewportState.DetachedByUser
            && evt.ActivationKind is TranscriptViewportActivationKind.WarmReturn or TranscriptViewportActivationKind.OverlayResume)
        {
            var detachedState = existing with
            {
                LastActivationGeneration = evt.Generation,
                RestorePending = true,
            };

            return Transition(
                evt,
                detachedState,
                isAutoFollowAttached: false,
                existing.Anchor is not null
                    ? TranscriptViewportCommandKind.RestoreAnchor
                    : TranscriptViewportCommandKind.None,
                existing.Anchor is not null
                    ? "WarmReturnRestoreAnchor"
                    : "WarmReturnPreserveDetached",
                existing.Anchor);
        }

        return Transition(
            evt,
            existing with
            {
                Mode = TranscriptViewportState.Settling,
                Anchor = null,
                LastActivationGeneration = evt.Generation,
                RestorePending = false,
            },
            isAutoFollowAttached: true,
            TranscriptViewportCommandKind.None,
            "SessionActivated");
    }

    private TranscriptViewportCommand HandleContextInvalidated(TranscriptViewportEvent.ConversationContextInvalidated evt)
    {
        var state = GetConversationStateOrDefault(evt.ConversationId, evt.Generation) with
        {
            RestorePending = false,
        };
        StoreConversationState(evt.ConversationId, state);

        _activeConversationId = null;
        _activeGeneration = evt.Generation;

        return ProjectTransientState(
            evt,
            TranscriptViewportState.Suspended,
            isAutoFollowAttached: false,
            TranscriptViewportCommandKind.StopProgrammaticScroll,
            "ContextInvalidated");
    }

    private TranscriptViewportCommand HandleUserDetached(TranscriptViewportEvent.UserDetached evt)
    {
        var state = new TranscriptViewportConversationState(
            TranscriptViewportState.DetachedByUser,
            evt.Anchor,
            LastKnownBottomState: false,
            evt.Generation,
            RestorePending: false);

        return Transition(
            evt,
            state,
            isAutoFollowAttached: false,
            TranscriptViewportCommandKind.MarkAutoFollowDetached,
            "ExplicitUserDetach",
            evt.Anchor);
    }

    private TranscriptViewportCommand HandleUserAttached(TranscriptViewportEvent.UserAttached evt)
    {
        var current = GetConversationStateOrDefault(evt.ConversationId, evt.Generation);
        if (current.Mode != TranscriptViewportState.DetachedByUser)
        {
            return None(evt, "AttachIgnored");
        }

        return Transition(
            evt,
            current with
            {
                Mode = TranscriptViewportState.Following,
                Anchor = null,
                RestorePending = false,
            },
            isAutoFollowAttached: true,
            TranscriptViewportCommandKind.MarkAutoFollowAttached,
            "ExplicitUserAttach");
    }

    private TranscriptViewportCommand HandleUserIntentScroll(TranscriptViewportEvent.UserIntentScroll evt)
    {
        var current = GetConversationStateOrDefault(evt.ConversationId, evt.Generation);
        var state = current with
        {
            Mode = TranscriptViewportState.DetachedByUser,
            Anchor = null,
            LastActivationGeneration = evt.Generation,
            RestorePending = false,
        };

        return Transition(
            evt,
            state,
            isAutoFollowAttached: false,
            TranscriptViewportCommandKind.MarkAutoFollowDetached,
            "ExplicitUserIntent",
            current.Anchor);
    }

    private TranscriptViewportCommand HandleTranscriptAppended(TranscriptViewportEvent.TranscriptAppended evt)
    {
        var current = GetConversationStateOrDefault(evt.ConversationId, evt.Generation);
        if (evt.AddedCount <= 0
            || current.Mode == TranscriptViewportState.Suspended
            || current.Mode == TranscriptViewportState.DetachedByUser)
        {
            return None(evt, "AppendIgnored");
        }

        if (_lastFacts.TryGetValue(evt.ConversationId, out var observed)
            && observed.Generation == evt.Generation
            && observed.Fact is { HasItems: true, IsReady: true, IsAtBottom: false, IsProgrammaticScrollInFlight: false })
        {
            return Transition(
                evt,
                current with
                {
                    Mode = TranscriptViewportState.Settling,
                    LastActivationGeneration = evt.Generation,
                },
                isAutoFollowAttached: true,
                TranscriptViewportCommandKind.IssueScrollToBottom,
                "AttachedAppendNeedsRecovery");
        }

        return None(evt, "AppendQueuedForViewportFact");
    }

    private TranscriptViewportCommand HandleViewportObserved(TranscriptViewportEvent.ViewportObserved evt)
    {
        _lastFacts[evt.ConversationId] = new TranscriptViewportObservedFact(evt.Generation, evt.Fact);

        var current = GetConversationStateOrDefault(evt.ConversationId, evt.Generation) with
        {
            LastKnownBottomState = evt.Fact.IsAtBottom,
            LastActivationGeneration = evt.Generation,
        };

        if (!evt.Fact.HasItems)
        {
            return Transition(
                evt,
                current with
                {
                    Mode = TranscriptViewportState.Idle,
                    Anchor = null,
                    RestorePending = false,
                },
                isAutoFollowAttached: true,
                TranscriptViewportCommandKind.None,
                "NoItems");
        }

        if (current.Mode == TranscriptViewportState.DetachedByUser)
        {
            return HandleDetachedViewportObserved(evt, current);
        }

        if (evt.Fact.IsReady && evt.Fact.IsAtBottom)
        {
            return Transition(
                evt,
                current with
                {
                    Mode = TranscriptViewportState.Following,
                    RestorePending = false,
                },
                isAutoFollowAttached: true,
                TranscriptViewportCommandKind.MarkAutoFollowAttached,
                "BottomConfirmed");
        }

        if (evt.Fact.IsProgrammaticScrollInFlight)
        {
            StoreConversationState(evt.ConversationId, current);
            return None(evt, "ProgrammaticScrollInFlight");
        }

        if (!evt.Fact.IsReady)
        {
            return Transition(
                evt,
                current with
                {
                    Mode = TranscriptViewportState.Settling,
                },
                isAutoFollowAttached: true,
                TranscriptViewportCommandKind.None,
                "ViewportNotReady");
        }

        return Transition(
            evt,
            current with
            {
                Mode = TranscriptViewportState.Settling,
            },
            isAutoFollowAttached: true,
            TranscriptViewportCommandKind.IssueScrollToBottom,
            "RecoverBottomWithoutUserDetach");
    }

    private TranscriptViewportCommand HandleDetachedViewportObserved(
        TranscriptViewportEvent.ViewportObserved evt,
        TranscriptViewportConversationState current)
    {
        if (evt.Fact.IsProgrammaticScrollInFlight)
        {
            StoreConversationState(evt.ConversationId, current);
            return None(evt, "DetachedProgrammaticScrollIgnored");
        }

        if (!evt.Fact.IsReady)
        {
            StoreConversationState(evt.ConversationId, current);
            return None(evt, "DetachedViewportNotReady");
        }

        if (current.RestorePending)
        {
            if (!evt.Fact.IsAtBottom)
            {
                StoreConversationState(evt.ConversationId, current with { RestorePending = false });
                return None(evt, "DetachedRestoreStabilized");
            }

            StoreConversationState(evt.ConversationId, current);
            return None(evt, "DetachedRestoreStillPending");
        }

        if (!evt.Fact.IsAtBottom)
        {
            StoreConversationState(evt.ConversationId, current with { RestorePending = false });
            return None(evt, "DetachedAwayFromBottom");
        }

        StoreConversationState(evt.ConversationId, current with { RestorePending = false });
        return None(evt, "DetachedBottomPreserved");
    }

    private bool MatchesActiveContext(string conversationId, int generation)
    {
        return !string.IsNullOrWhiteSpace(_activeConversationId)
            && string.Equals(_activeConversationId, conversationId, StringComparison.Ordinal)
            && generation == _activeGeneration;
    }

    private TranscriptViewportConversationState GetConversationStateOrDefault(string conversationId, int generation)
    {
        if (_conversationStates.TryGetValue(conversationId, out var state))
        {
            return state;
        }

        return new TranscriptViewportConversationState(
            TranscriptViewportState.Idle,
            Anchor: null,
            LastKnownBottomState: true,
            LastActivationGeneration: generation,
            RestorePending: false);
    }

    private TranscriptViewportCommand Transition(
        TranscriptViewportEvent evt,
        TranscriptViewportConversationState targetState,
        bool isAutoFollowAttached,
        TranscriptViewportCommandKind commandKind,
        string reason,
        TranscriptViewportAnchor? anchor = null)
    {
        var previousState = State;
        var previousAttached = IsAutoFollowAttached;

        StoreConversationState(evt.ConversationId, targetState);
        ApplyPublicState(targetState.Mode, isAutoFollowAttached);

        TranscriptViewportTransition? transition = null;
        if (previousState != State || previousAttached != IsAutoFollowAttached)
        {
            transition = new TranscriptViewportTransition(
                previousState,
                State,
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

        return new TranscriptViewportCommand(commandKind, evt.ConversationId, evt.Generation, reason, transition, anchor);
    }

    private TranscriptViewportCommand ProjectTransientState(
        TranscriptViewportEvent evt,
        TranscriptViewportState targetState,
        bool isAutoFollowAttached,
        TranscriptViewportCommandKind commandKind,
        string reason)
    {
        var previousState = State;
        var previousAttached = IsAutoFollowAttached;

        ApplyPublicState(targetState, isAutoFollowAttached);

        TranscriptViewportTransition? transition = null;
        if (previousState != State || previousAttached != IsAutoFollowAttached)
        {
            transition = new TranscriptViewportTransition(
                previousState,
                State,
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

    private void StoreConversationState(string conversationId, TranscriptViewportConversationState state)
    {
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            _conversationStates[conversationId] = state;
        }
    }

    private void ApplyPublicState(TranscriptViewportState state, bool isAutoFollowAttached)
    {
        State = state;
        IsAutoFollowAttached = isAutoFollowAttached;
    }

    private static TranscriptViewportCommand None(TranscriptViewportEvent evt, string reason)
    {
        return new TranscriptViewportCommand(TranscriptViewportCommandKind.None, evt.ConversationId, evt.Generation, reason);
    }

    private readonly record struct TranscriptViewportObservedFact(int Generation, TranscriptViewportFact Fact);
}
