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

    public TranscriptViewportConversationState? GetConversationState(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        return _conversationStates.TryGetValue(conversationId, out var state)
            ? state
            : null;
    }

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
            TranscriptViewportEvent.ProjectionReady projectionReady => HandleProjectionReady(projectionReady),
            TranscriptViewportEvent.RestoreConfirmed restoreConfirmed => HandleRestoreConfirmed(restoreConfirmed),
            TranscriptViewportEvent.RestoreUnavailable restoreUnavailable => HandleRestoreUnavailable(restoreUnavailable),
            TranscriptViewportEvent.RestoreAbandoned restoreAbandoned => HandleRestoreAbandoned(restoreAbandoned),
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
                    RestorePending: false,
                    RestoreToken: null,
                    PendingProjectionEpoch: null),
                isAutoFollowAttached: true,
                TranscriptViewportCommandKind.None,
                "SessionActivatedEmpty");
        }

        var existing = GetConversationStateOrDefault(evt.ConversationId, evt.Generation);

        if (existing.Mode == TranscriptViewportState.DetachedByUser
            && evt.ActivationKind is TranscriptViewportActivationKind.WarmReturn or TranscriptViewportActivationKind.OverlayResume)
        {
            var hasRestoreToken = existing.RestoreToken is not null;
            var detachedState = existing with
            {
                Mode = hasRestoreToken
                    ? TranscriptViewportState.DetachedPendingRestore
                    : TranscriptViewportState.DetachedByUser,
                LastActivationGeneration = evt.Generation,
                RestorePending = false,
                PendingProjectionEpoch = existing.RestoreToken?.ProjectionEpoch,
            };

            return Transition(
                evt,
                detachedState,
                isAutoFollowAttached: false,
                TranscriptViewportCommandKind.None,
                hasRestoreToken
                    ? "WarmReturnPendingRestore"
                    : "WarmReturnPreserveDetached");
        }

        return Transition(
            evt,
            existing with
            {
                Mode = TranscriptViewportState.Settling,
                Anchor = null,
                LastActivationGeneration = evt.Generation,
                RestorePending = false,
                RestoreToken = null,
                PendingProjectionEpoch = null,
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
            PendingProjectionEpoch = null,
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
            RestorePending: false,
            RestoreToken: evt.RestoreToken,
            PendingProjectionEpoch: null);

        return Transition(
            evt,
            state,
            isAutoFollowAttached: false,
            TranscriptViewportCommandKind.MarkAutoFollowDetached,
            "ExplicitUserDetach");
    }

    private TranscriptViewportCommand HandleUserAttached(TranscriptViewportEvent.UserAttached evt)
    {
        var current = GetConversationStateOrDefault(evt.ConversationId, evt.Generation);
        if (!IsDetachedMode(current.Mode))
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
                RestoreToken = null,
                PendingProjectionEpoch = null,
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
            RestoreToken = null,
            PendingProjectionEpoch = null,
        };

        return Transition(
            evt,
            state,
            isAutoFollowAttached: false,
            TranscriptViewportCommandKind.MarkAutoFollowDetached,
            "ExplicitUserIntent");
    }

    private TranscriptViewportCommand HandleTranscriptAppended(TranscriptViewportEvent.TranscriptAppended evt)
    {
        var current = GetConversationStateOrDefault(evt.ConversationId, evt.Generation);
        if (evt.AddedCount <= 0
            || current.Mode == TranscriptViewportState.Suspended
            || IsDetachedMode(current.Mode))
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

        if (IsDetachedMode(current.Mode))
        {
            return HandleDetachedViewportObserved(evt, current);
        }

        if (!evt.Fact.HasItems)
        {
            return Transition(
                evt,
                current with
                {
                    Mode = TranscriptViewportState.Idle,
                    Anchor = null,
                    RestorePending = false,
                    RestoreToken = null,
                    PendingProjectionEpoch = null,
                },
                isAutoFollowAttached: true,
                TranscriptViewportCommandKind.None,
                "NoItems");
        }

        if (evt.Fact.IsReady && evt.Fact.IsAtBottom)
        {
            return Transition(
                evt,
                current with
                {
                    Mode = TranscriptViewportState.Following,
                    RestorePending = false,
                    PendingProjectionEpoch = null,
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
        if (current.Mode == TranscriptViewportState.DetachedPendingRestore)
        {
            StoreConversationState(evt.ConversationId, current);
            return None(evt, evt.Fact.IsReady
                ? "DetachedPendingRestoreAwaitingProjectionReady"
                : "DetachedPendingRestoreViewportNotReady");
        }

        if (current.Mode == TranscriptViewportState.DetachedRestoring)
        {
            StoreConversationState(evt.ConversationId, current);
            return None(evt, evt.Fact.IsProgrammaticScrollInFlight
                ? "DetachedRestoringInFlight"
                : "DetachedRestoringAwaitingOutcome");
        }

        if (evt.Fact.IsProgrammaticScrollInFlight)
        {
            StoreConversationState(evt.ConversationId, current);
            return None(evt, "DetachedProgrammaticScrollIgnored");
        }

        if (!evt.Fact.HasItems)
        {
            StoreConversationState(evt.ConversationId, current);
            return None(evt, "DetachedNoItems");
        }

        if (!evt.Fact.IsReady)
        {
            StoreConversationState(evt.ConversationId, current);
            return None(evt, "DetachedViewportNotReady");
        }

        if (!evt.Fact.IsAtBottom)
        {
            StoreConversationState(evt.ConversationId, current);
            return None(evt, "DetachedAwayFromBottom");
        }

        StoreConversationState(evt.ConversationId, current);
        return None(evt, "DetachedBottomPreserved");
    }

    private TranscriptViewportCommand HandleProjectionReady(TranscriptViewportEvent.ProjectionReady evt)
    {
        var current = GetConversationStateOrDefault(evt.ConversationId, evt.Generation);
        if (current.Mode != TranscriptViewportState.DetachedPendingRestore
            || current.RestoreToken is not { } token
            || !string.Equals(token.ConversationId, evt.ConversationId, StringComparison.Ordinal)
            || token.ProjectionEpoch > evt.ProjectionEpoch)
        {
            return None(evt, "ProjectionReadyIgnored");
        }

        return Transition(
            evt,
            current with
            {
                Mode = TranscriptViewportState.DetachedRestoring,
                PendingProjectionEpoch = evt.ProjectionEpoch,
            },
            isAutoFollowAttached: false,
            TranscriptViewportCommandKind.RequestRestore,
            "ProjectionReadyDispatchRestore",
            restoreToken: token);
    }

    private TranscriptViewportCommand HandleRestoreConfirmed(TranscriptViewportEvent.RestoreConfirmed evt)
    {
        var current = GetConversationStateOrDefault(evt.ConversationId, evt.Generation);
        if (current.Mode != TranscriptViewportState.DetachedRestoring
            || current.RestoreToken is not { } token
            || token != evt.RestoreToken)
        {
            return None(evt, "RestoreConfirmedIgnored");
        }

        return Transition(
            evt,
            current with
            {
                Mode = TranscriptViewportState.DetachedByUser,
                PendingProjectionEpoch = null,
            },
            isAutoFollowAttached: false,
            TranscriptViewportCommandKind.None,
            "RestoreConfirmed");
    }

    private TranscriptViewportCommand HandleRestoreUnavailable(TranscriptViewportEvent.RestoreUnavailable evt)
    {
        return CompleteRestoreAttempt(evt, evt.Reason);
    }

    private TranscriptViewportCommand HandleRestoreAbandoned(TranscriptViewportEvent.RestoreAbandoned evt)
    {
        return CompleteRestoreAttempt(evt, evt.Reason);
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
            RestorePending: false,
            RestoreToken: null,
            PendingProjectionEpoch: null);
    }

    private TranscriptViewportCommand Transition(
        TranscriptViewportEvent evt,
        TranscriptViewportConversationState targetState,
        bool isAutoFollowAttached,
        TranscriptViewportCommandKind commandKind,
        string reason,
        TranscriptViewportAnchor? anchor = null,
        TranscriptProjectionRestoreToken? restoreToken = null)
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

        return new TranscriptViewportCommand(commandKind, evt.ConversationId, evt.Generation, reason, transition, anchor, restoreToken);
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

    private TranscriptViewportCommand CompleteRestoreAttempt(TranscriptViewportEvent evt, string reason)
    {
        var current = GetConversationStateOrDefault(evt.ConversationId, evt.Generation);
        if (current.Mode != TranscriptViewportState.DetachedRestoring)
        {
            return None(evt, "RestoreOutcomeIgnored");
        }

        return Transition(
            evt,
            current with
            {
                Mode = TranscriptViewportState.DetachedByUser,
                PendingProjectionEpoch = null,
            },
            isAutoFollowAttached: false,
            TranscriptViewportCommandKind.None,
            reason);
    }

    private static bool IsDetachedMode(TranscriptViewportState state)
    {
        return state is TranscriptViewportState.DetachedByUser
            or TranscriptViewportState.DetachedPendingRestore
            or TranscriptViewportState.DetachedRestoring;
    }

    private readonly record struct TranscriptViewportObservedFact(int Generation, TranscriptViewportFact Fact);
}
