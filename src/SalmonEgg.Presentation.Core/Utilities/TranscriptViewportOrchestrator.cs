namespace SalmonEgg.Presentation.Utilities;

public readonly record struct TranscriptViewportOrchestratorSnapshot(
    TranscriptViewportState State,
    bool IsAutoFollowAttached,
    bool IsViewportDetached,
    bool HasPendingSettle,
    bool IsProgrammaticScrollInFlight,
    bool AttachToBottomIntentPending,
    bool UserScrollIntentPending,
    bool UserScrollIntentCompleted,
    bool ScrollToBottomScheduled,
    int Generation,
    int ScheduledScrollRequestVersion,
    int ActiveScrollGeneration);

public readonly record struct TranscriptViewportObservationResult(
    TranscriptViewportCommand Command);

public readonly record struct TranscriptScrollRequestToken(int Generation, string ConversationId);

public readonly record struct TranscriptScrollScheduleToken(int Generation, int RequestVersion, string ConversationId);

public sealed class TranscriptViewportOrchestrator
{
    private readonly TranscriptScrollSettler _scrollSettler = new();
    private readonly TranscriptViewportCoordinator _viewportCoordinator = new();
    private bool _attachToBottomIntentPending;
    private bool _userScrollIntentPending;
    private bool _userScrollIntentCompleted;
    private bool _suspendAutoScrollTracking;
    private bool _scrollToBottomScheduled;
    private int _scrollScheduleGeneration;
    private int _scheduledScrollRequestVersion;
    private int _activeTranscriptScrollGeneration = -1;

    public TranscriptViewportOrchestratorSnapshot Snapshot => new(
        _viewportCoordinator.State,
        _viewportCoordinator.IsAutoFollowAttached,
        IsViewportDetached,
        _scrollSettler.HasPendingWork,
        IsProgrammaticScrollInFlight,
        _attachToBottomIntentPending,
        _userScrollIntentPending,
        _userScrollIntentCompleted,
        _scrollToBottomScheduled,
        _scrollScheduleGeneration,
        _scheduledScrollRequestVersion,
        _activeTranscriptScrollGeneration);

    public bool IsViewportDetached
        => _viewportCoordinator.State is TranscriptViewportState.DetachedByUser
            or TranscriptViewportState.DetachedPendingRestore
            or TranscriptViewportState.DetachedRestoring;

    public TranscriptViewportState State => _viewportCoordinator.State;

    public bool IsAutoFollowAttached => _viewportCoordinator.IsAutoFollowAttached;

    public bool HasPendingSettle => _scrollSettler.HasPendingWork;

    public bool HasActiveScrollGeneration => _activeTranscriptScrollGeneration >= 0;

    public int Generation => _scrollScheduleGeneration;

    public bool IsScrollToBottomScheduled => _scrollToBottomScheduled;

    public bool AttachToBottomIntentPending => _attachToBottomIntentPending;

    public bool UserScrollIntentPending => _userScrollIntentPending;

    public bool UserScrollIntentCompleted => _userScrollIntentCompleted;

    public bool IsProgrammaticScrollInFlight
        => _suspendAutoScrollTracking
            || _scrollToBottomScheduled
            || _activeTranscriptScrollGeneration >= 0;

    public TranscriptViewportTransition? LastTransition => _viewportCoordinator.LastTransition;

    public TranscriptViewportConversationState? GetConversationState(string conversationId)
        => _viewportCoordinator.GetConversationState(conversationId);

    public void StartLifecycleGeneration()
    {
        unchecked { _scrollScheduleGeneration++; }
    }

    public void ResetInteractionState()
    {
        _attachToBottomIntentPending = false;
        _userScrollIntentPending = false;
        _userScrollIntentCompleted = false;
    }

    public void ResetScheduledScrollState()
    {
        _scrollToBottomScheduled = false;
        _activeTranscriptScrollGeneration = -1;
    }

    public void StopProgrammaticScroll()
    {
        unchecked { _scrollScheduleGeneration++; }
        _scrollToBottomScheduled = false;
        _activeTranscriptScrollGeneration = -1;
        _suspendAutoScrollTracking = false;
    }

    public void ResetForConversationChange()
    {
        unchecked { _scrollScheduleGeneration++; }
        _activeTranscriptScrollGeneration = -1;
        _suspendAutoScrollTracking = false;
        _scrollToBottomScheduled = false;
        unchecked { _scheduledScrollRequestVersion++; }
        ResetInteractionState();
    }

    public void BeginSettleRound(string? sessionId)
    {
        _activeTranscriptScrollGeneration = -1;
        _scrollSettler.BeginRound(sessionId);
    }

    public TranscriptScrollDecision TryIssueScrollRequest(string? sessionId, bool hasMessages, bool isReady)
    {
        if (_activeTranscriptScrollGeneration >= 0 || IsViewportDetached)
        {
            return default;
        }

        var decision = _scrollSettler.TryIssueScrollRequest(sessionId, hasMessages, isReady);
        if (decision.Action == TranscriptScrollAction.IssueScrollRequest)
        {
            _activeTranscriptScrollGeneration = decision.Generation;
            _suspendAutoScrollTracking = true;
        }
        else if (decision.Action is TranscriptScrollAction.Completed
            or TranscriptScrollAction.Aborted
            or TranscriptScrollAction.Exhausted)
        {
            _activeTranscriptScrollGeneration = -1;
            ReleaseAutoScrollTracking();
        }

        return decision;
    }

    public bool TryCaptureActiveScrollRequestToken(string? conversationId, out TranscriptScrollRequestToken token)
    {
        if (_activeTranscriptScrollGeneration < 0 || string.IsNullOrWhiteSpace(conversationId))
        {
            token = default;
            return false;
        }

        token = new TranscriptScrollRequestToken(_activeTranscriptScrollGeneration, conversationId);
        return true;
    }

    public bool MatchesActiveScrollRequest(TranscriptScrollRequestToken token, string? conversationId)
        => _activeTranscriptScrollGeneration >= 0
            && token.Generation == _activeTranscriptScrollGeneration
            && !string.IsNullOrWhiteSpace(conversationId)
            && string.Equals(token.ConversationId, conversationId, StringComparison.Ordinal);

    public TranscriptScrollDecision ReportSettled(
        string? sessionId,
        TranscriptScrollSettleObservation observation)
    {
        if (_activeTranscriptScrollGeneration < 0)
        {
            return default;
        }

        var generation = _activeTranscriptScrollGeneration;
        _activeTranscriptScrollGeneration = -1;
        var decision = _scrollSettler.ReportSettled(sessionId, generation, observation);
        if (decision.Action == TranscriptScrollAction.IssueScrollRequest)
        {
            _activeTranscriptScrollGeneration = decision.Generation;
            _suspendAutoScrollTracking = true;
        }
        else if (decision.Action is TranscriptScrollAction.Completed
            or TranscriptScrollAction.Aborted
            or TranscriptScrollAction.Exhausted)
        {
            ReleaseAutoScrollTracking();
        }

        return decision;
    }

    public TranscriptScrollDecision ApplySettleDecision(TranscriptScrollDecision decision)
    {
        if (decision.Action == TranscriptScrollAction.IssueScrollRequest)
        {
            _activeTranscriptScrollGeneration = decision.Generation;
            _suspendAutoScrollTracking = true;
        }
        else if (decision.Action is TranscriptScrollAction.Completed
            or TranscriptScrollAction.Aborted
            or TranscriptScrollAction.Exhausted)
        {
            ReleaseAutoScrollTracking();
        }

        return decision;
    }

    public TranscriptScrollDecision ReportSettled(
        string? sessionId,
        int generation,
        TranscriptScrollSettleObservation observation)
    {
        if (_activeTranscriptScrollGeneration == generation)
        {
            _activeTranscriptScrollGeneration = -1;
        }

        var decision = _scrollSettler.ReportSettled(sessionId, generation, observation);
        if (decision.Action == TranscriptScrollAction.IssueScrollRequest)
        {
            _activeTranscriptScrollGeneration = decision.Generation;
            _suspendAutoScrollTracking = true;
        }
        else if (decision.Action is TranscriptScrollAction.Completed
            or TranscriptScrollAction.Aborted
            or TranscriptScrollAction.Exhausted)
        {
            _activeTranscriptScrollGeneration = -1;
            ReleaseAutoScrollTracking();
        }

        return decision;
    }

    public TranscriptScrollDecision AbortSettleForUserInteraction()
    {
        _activeTranscriptScrollGeneration = -1;
        var decision = _scrollSettler.AbortForUserInteraction();
        if (decision.Action == TranscriptScrollAction.Aborted)
        {
            ReleaseAutoScrollTracking();
        }

        return decision;
    }

    public void StopInitialScrollForManualInteraction()
    {
        _suspendAutoScrollTracking = false;
        _scrollToBottomScheduled = false;
        unchecked { _scheduledScrollRequestVersion++; }
    }

    public void ReleaseAutoScrollTracking()
    {
        _suspendAutoScrollTracking = false;
    }

    public void MarkScrollToBottomScheduled()
    {
        _scrollToBottomScheduled = true;
    }

    public bool TryBeginScrollToBottomSchedule(string? conversationId, out TranscriptScrollScheduleToken token)
    {
        if (_scrollToBottomScheduled || string.IsNullOrWhiteSpace(conversationId))
        {
            token = default;
            return false;
        }

        _scrollToBottomScheduled = true;
        token = new TranscriptScrollScheduleToken(_scrollScheduleGeneration, _scheduledScrollRequestVersion, conversationId);
        return true;
    }

    public void ClearScrollToBottomScheduled()
    {
        _scrollToBottomScheduled = false;
    }

    public void ReleaseScrollToBottomSchedule(TranscriptScrollScheduleToken token)
    {
        if (_scrollScheduleGeneration == token.Generation
            && _scheduledScrollRequestVersion == token.RequestVersion
            && _scrollToBottomScheduled)
        {
            _scrollToBottomScheduled = false;
        }
    }

    public bool CanExecuteScrollToBottomSchedule(TranscriptScrollScheduleToken token, string? conversationId)
        => _scrollScheduleGeneration == token.Generation
            && _scheduledScrollRequestVersion == token.RequestVersion
            && _scrollToBottomScheduled
            && IsAutoFollowAttached
            && !string.IsNullOrWhiteSpace(conversationId)
            && string.Equals(token.ConversationId, conversationId, StringComparison.Ordinal);

    public void MarkProjectionRestoreQueued()
    {
        _suspendAutoScrollTracking = true;
        _scrollToBottomScheduled = false;
    }

    public void MarkAttachToBottomIntent()
    {
        _attachToBottomIntentPending = true;
        _userScrollIntentCompleted = true;
    }

    public void MarkDetachedViewportInteractionStarted()
    {
        _attachToBottomIntentPending = true;
        _userScrollIntentCompleted = false;
    }

    public void MarkUserScrollIntentStarted()
    {
        _userScrollIntentPending = true;
        _userScrollIntentCompleted = false;
    }

    public void MarkUserScrollIntentCompleted()
    {
        _userScrollIntentCompleted = true;
    }

    public void ClearUserScrollIntent()
    {
        _userScrollIntentPending = false;
        _userScrollIntentCompleted = false;
    }

    public void ClearAttachIntent()
    {
        _attachToBottomIntentPending = false;
        _userScrollIntentCompleted = false;
    }

    public void ClearAttachIntentOnly()
    {
        _attachToBottomIntentPending = false;
    }

    public TranscriptViewportObservationResult ObserveViewportFact(
        string conversationId,
        TranscriptViewportFact fact,
        TranscriptProjectionRestoreToken? restoreToken)
    {
        if (IsViewportDetached
            && _attachToBottomIntentPending
            && !fact.IsProgrammaticScrollInFlight)
        {
            if (fact.IsAtBottom)
            {
                ClearAttachIntent();
                return new(Handle(CreateUserAttachedEvent(conversationId)));
            }

            if (_userScrollIntentCompleted)
            {
                ClearAttachIntent();
            }
        }

        if (ShouldDetachForNativeViewportMovement(fact))
        {
            StopInitialScrollForManualInteraction();
            ApplyUserInteractionAbort();
            return DetachFromViewportMovement(conversationId, restoreToken);
        }

        if (_userScrollIntentPending)
        {
            if (!fact.IsAtBottom)
            {
                ClearUserScrollIntent();
                if (fact.IsProgrammaticScrollInFlight)
                {
                    StopInitialScrollForManualInteraction();
                    ApplyUserInteractionAbort();
                }

                return DetachFromViewportMovement(conversationId, restoreToken);
            }

            if (!fact.IsProgrammaticScrollInFlight && _userScrollIntentCompleted)
            {
                ClearUserScrollIntent();
            }
        }

        if (ShouldRefreshDetachedRestoreToken(fact)
            && restoreToken is { } updatedRestoreToken
            && GetConversationState(conversationId)?.RestoreToken != updatedRestoreToken)
        {
            return new(Handle(CreateUserDetachedEvent(conversationId, updatedRestoreToken)));
        }

        return new(Handle(CreateViewportFactChangedEvent(conversationId, fact)));
    }

    public TranscriptViewportCommand Activate(string conversationId, TranscriptViewportActivationKind activationKind)
        => _viewportCoordinator.Handle(new TranscriptViewportEvent.SessionActivated(
            conversationId,
            _scrollScheduleGeneration,
            activationKind));

    public TranscriptViewportCommand InvalidateContext(string conversationId)
        => string.IsNullOrWhiteSpace(conversationId)
            ? default
            : _viewportCoordinator.Handle(new TranscriptViewportEvent.ConversationContextInvalidated(
                conversationId,
                _scrollScheduleGeneration));

    public TranscriptViewportCommand Handle(TranscriptViewportEvent evt)
        => _viewportCoordinator.Handle(evt);

    public TranscriptViewportEvent.UserDetached CreateUserDetachedEvent(
        string conversationId,
        TranscriptProjectionRestoreToken restoreToken)
        => new(conversationId, _scrollScheduleGeneration, restoreToken);

    public TranscriptViewportEvent.UserIntentScroll CreateUserIntentScrollEvent(string conversationId)
        => new(conversationId, _scrollScheduleGeneration);

    public TranscriptViewportEvent.UserAttached CreateUserAttachedEvent(string conversationId)
        => new(conversationId, _scrollScheduleGeneration);

    public TranscriptViewportEvent.ViewportFactChanged CreateViewportFactChangedEvent(
        string conversationId,
        TranscriptViewportFact fact)
        => new(conversationId, _scrollScheduleGeneration, fact);

    public TranscriptViewportEvent.TranscriptAppended CreateTranscriptAppendedEvent(
        string conversationId,
        int addedCount)
        => new(conversationId, _scrollScheduleGeneration, addedCount);

    public TranscriptViewportEvent.ProjectionReady CreateProjectionReadyEvent(
        string conversationId,
        long projectionEpoch)
        => new(conversationId, _scrollScheduleGeneration, projectionEpoch);

    public TranscriptViewportEvent.RestoreConfirmed CreateRestoreConfirmedEvent(
        string conversationId,
        int generation,
        TranscriptProjectionRestoreToken restoreToken)
        => new(conversationId, generation, restoreToken);

    public TranscriptViewportEvent.RestoreUnavailable CreateRestoreUnavailableEvent(
        string conversationId,
        int generation,
        string reason)
        => new(conversationId, generation, reason);

    public TranscriptViewportEvent.RestoreAbandoned CreateRestoreAbandonedEvent(
        string conversationId,
        int generation,
        string reason)
        => new(conversationId, generation, reason);

    private TranscriptViewportObservationResult DetachFromViewportMovement(
        string conversationId,
        TranscriptProjectionRestoreToken? restoreToken)
    {
        ClearAttachIntentOnly();
        var command = restoreToken is { } capturedRestoreToken
            ? Handle(CreateUserDetachedEvent(conversationId, capturedRestoreToken))
            : Handle(CreateUserIntentScrollEvent(conversationId));

        return new(command);
    }

    private void ApplyUserInteractionAbort()
    {
        _activeTranscriptScrollGeneration = -1;
        var decision = _scrollSettler.AbortForUserInteraction();
        if (decision.Action == TranscriptScrollAction.Aborted)
        {
            ReleaseAutoScrollTracking();
        }
    }

    private bool ShouldDetachForNativeViewportMovement(TranscriptViewportFact fact)
        => IsAutoFollowAttached
            && !HasPendingSettle
            && fact.HasItems
            && !fact.IsAtBottom
            && !fact.IsProgrammaticScrollInFlight
            && (_userScrollIntentPending || _userScrollIntentCompleted || _attachToBottomIntentPending);

    private bool ShouldRefreshDetachedRestoreToken(TranscriptViewportFact fact)
        => State == TranscriptViewportState.DetachedByUser
            && !fact.IsProgrammaticScrollInFlight
            && fact.HasItems
            && fact.IsReady
            && !fact.IsAtBottom;
}
