namespace SalmonEgg.Presentation.Utilities;

public readonly record struct TranscriptViewportViewState(
    bool IsViewReady,
    bool IsViewportReady,
    bool HasMessages,
    bool IsAtBottom,
    bool IsLastItemVisibleAtBottom);

public enum TranscriptViewportControllerActionKind
{
    None = 0,
    ScrollLastMessageIntoView = 1,
    StopProgrammaticScroll = 2,
    AutoFollowAttached = 3,
    AutoFollowDetached = 4,
    RequestRestore = 5,
}

public readonly record struct TranscriptViewportControllerAction(
    TranscriptViewportControllerActionKind Kind,
    TranscriptScrollRequestToken ScrollRequestToken = default,
    TranscriptProjectionRestoreToken? RestoreToken = null,
    int Generation = -1);

public sealed class TranscriptViewportController
{
    private readonly TranscriptViewportOrchestrator _orchestrator = new();
    private string _conversationId = string.Empty;
    private bool _isLoaded;
    private bool _isSessionActive;
    private bool _isOverlayVisible;

    public TranscriptViewportOrchestratorSnapshot Snapshot => _orchestrator.Snapshot;

    public TranscriptViewportState State => _orchestrator.State;

    public bool IsViewportDetached => _orchestrator.IsViewportDetached;

    public bool IsAutoFollowAttached => _orchestrator.IsAutoFollowAttached;

    public bool HasPendingSettle => _orchestrator.HasPendingSettle;

    public bool HasActiveScrollGeneration => _orchestrator.HasActiveScrollGeneration;

    public bool IsProgrammaticScrollInFlight => _orchestrator.IsProgrammaticScrollInFlight;

    public bool AttachToBottomIntentPending => _orchestrator.AttachToBottomIntentPending;

    public bool UserScrollIntentPending => _orchestrator.UserScrollIntentPending;

    public bool UserScrollIntentCompleted => _orchestrator.UserScrollIntentCompleted;

    public int Generation => _orchestrator.Generation;

    public TranscriptViewportTransition? LastTransition => _orchestrator.LastTransition;

    public TranscriptViewportConversationState? GetConversationState(string conversationId)
        => _orchestrator.GetConversationState(conversationId);

    public void MarkProjectionRestoreQueued()
        => _orchestrator.MarkProjectionRestoreQueued();

    public void MarkDetachedViewportInteractionStarted()
        => _orchestrator.MarkDetachedViewportInteractionStarted();

    public void MarkUserScrollIntentStarted()
        => _orchestrator.MarkUserScrollIntentStarted();

    public void MarkUserScrollIntentCompleted()
        => _orchestrator.MarkUserScrollIntentCompleted();

    public void Load(
        string? conversationId,
        bool isSessionActive,
        bool isOverlayVisible,
        bool hasMessages)
    {
        _isLoaded = true;
        _orchestrator.StartLifecycleGeneration();
        _orchestrator.ResetInteractionState();
        _conversationId = ResolveAuthoritativeConversationId(conversationId, isSessionActive);
        _isSessionActive = isSessionActive;
        _isOverlayVisible = isOverlayVisible;
        _ = ApplyCommand(_orchestrator.Activate(_conversationId, ResolveInitialActivationKind(_conversationId)));
        if (!_isOverlayVisible && hasMessages && !_orchestrator.IsViewportDetached)
        {
            _orchestrator.BeginSettleRound(_conversationId);
        }
    }

    public IReadOnlyList<TranscriptViewportControllerAction> Unload()
    {
        var actions = new List<TranscriptViewportControllerAction>();
        actions.AddRange(AbandonContext("ViewUnloaded"));
        _isLoaded = false;
        _orchestrator.StartLifecycleGeneration();
        _orchestrator.ResetInteractionState();
        _orchestrator.ResetScheduledScrollState();
        _ = ApplyCommand(_orchestrator.InvalidateContext(_conversationId));
        _conversationId = string.Empty;
        _isSessionActive = false;
        _isOverlayVisible = false;
        return actions;
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnConversationChanged(
        string? conversationId,
        bool isSessionActive,
        bool isOverlayVisible,
        bool hasMessages)
    {
        var actions = new List<TranscriptViewportControllerAction>();
        actions.AddRange(AbandonContext("ConversationChanged"));
        if (!string.IsNullOrWhiteSpace(_conversationId))
        {
            actions.AddRange(ApplyCommand(_orchestrator.InvalidateContext(_conversationId)));
        }

        _orchestrator.ResetForConversationChange();
        _conversationId = ResolveAuthoritativeConversationId(conversationId, isSessionActive);
        _isSessionActive = isSessionActive;
        _isOverlayVisible = isOverlayVisible;
        _orchestrator.ClearUserScrollIntent();
        actions.AddRange(ApplyCommand(_orchestrator.Activate(
            _conversationId,
            TranscriptViewportActivationKind.WarmReturn)));
        if (!_orchestrator.IsViewportDetached && CanTrackViewport(hasMessages))
        {
            _orchestrator.BeginSettleRound(_conversationId);
        }

        actions.AddRange(TryIssueScrollRequest(CreateViewStateFromAvailability(hasMessages)));
        return actions;
    }

    public IReadOnlyList<TranscriptViewportControllerAction> ActivateCurrentConversation(
        string? conversationId,
        bool isSessionActive,
        bool isOverlayVisible,
        bool hasMessages,
        TranscriptViewportActivationKind activationKind)
    {
        _conversationId = ResolveAuthoritativeConversationId(conversationId, isSessionActive);
        _isSessionActive = isSessionActive;
        _isOverlayVisible = isOverlayVisible;
        var actions = new List<TranscriptViewportControllerAction>();
        actions.AddRange(ApplyCommand(_orchestrator.Activate(
            _conversationId,
            activationKind)));
        if (!_orchestrator.IsViewportDetached && CanTrackViewport(hasMessages))
        {
            _orchestrator.BeginSettleRound(_conversationId);
        }

        actions.AddRange(TryIssueScrollRequest(CreateViewStateFromAvailability(hasMessages)));
        return actions;
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnMessagesAppended(
        int addedCount,
        TranscriptViewportViewState viewState)
    {
        var actions = new List<TranscriptViewportControllerAction>();
        if (!_isLoaded)
        {
            return actions;
        }

        if (_isSessionActive
            && viewState.HasMessages
            && !_orchestrator.HasPendingSettle
            && !_orchestrator.IsViewportDetached)
        {
            _orchestrator.BeginSettleRound(_conversationId);
        }

        if (_orchestrator.HasPendingSettle && _orchestrator.IsViewportDetached)
        {
            actions.AddRange(ApplyScrollDecision(_orchestrator.AbortSettleForUserInteraction(), viewState));
            return actions;
        }

        actions.AddRange(TryIssueScrollRequest(viewState));
        if (actions.Count > 0)
        {
            return actions;
        }

        actions.AddRange(ApplyCommand(_orchestrator.Handle(
            _orchestrator.CreateTranscriptAppendedEvent(_conversationId, addedCount))));
        return actions;
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnViewportChanged(
        TranscriptViewportViewState viewState,
        TranscriptProjectionRestoreToken? restoreToken = null)
    {
        var actions = new List<TranscriptViewportControllerAction>();
        if (!CanObserveViewport(viewState))
        {
            return actions;
        }

        if (_orchestrator.HasPendingSettle && _orchestrator.IsViewportDetached)
        {
            actions.AddRange(ApplyScrollDecision(_orchestrator.AbortSettleForUserInteraction(), viewState));
            actions.AddRange(ObserveViewport(viewState, restoreToken));
            return actions;
        }

        if (_orchestrator.HasPendingSettle)
        {
            actions.AddRange(OnScheduledScrollObservationCore(viewState));
            actions.AddRange(TryIssueScrollRequest(viewState));
        }

        actions.AddRange(ObserveViewport(viewState, restoreToken));
        return actions;
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnUserViewportIntent(
        TranscriptViewportViewState viewState,
        TranscriptProjectionRestoreToken? restoreToken = null)
    {
        var actions = new List<TranscriptViewportControllerAction>();
        actions.AddRange(AbandonContext("UserInterrupted"));

        if (_orchestrator.IsViewportDetached)
        {
            _orchestrator.MarkAttachToBottomIntent();
            if (viewState.IsAtBottom)
            {
                _orchestrator.ClearAttachIntent();
                actions.AddRange(ApplyCommand(_orchestrator.Handle(
                    _orchestrator.CreateUserAttachedEvent(_conversationId))));
            }

            return actions;
        }

        if (viewState.IsAtBottom)
        {
            _orchestrator.MarkUserScrollIntentStarted();
            _orchestrator.StopInitialScrollForManualInteraction();
            return actions;
        }

        _orchestrator.ClearAttachIntentOnly();
        var command = restoreToken is { } token
            ? _orchestrator.Handle(_orchestrator.CreateUserDetachedEvent(_conversationId, token))
            : _orchestrator.Handle(_orchestrator.CreateUserIntentScrollEvent(_conversationId));
        actions.AddRange(ApplyCommand(command));
        _orchestrator.StopInitialScrollForManualInteraction();
        if (_orchestrator.HasPendingSettle)
        {
            actions.AddRange(ApplyScrollDecision(_orchestrator.AbortSettleForUserInteraction(), viewState));
        }

        return actions;
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnUserViewportDetachIntent(
        TranscriptViewportViewState viewState,
        TranscriptProjectionRestoreToken? restoreToken = null)
    {
        var actions = new List<TranscriptViewportControllerAction>();
        actions.AddRange(AbandonContext("UserInterrupted"));

        if (_orchestrator.IsViewportDetached)
        {
            _orchestrator.MarkAttachToBottomIntent();
            return actions;
        }

        _orchestrator.MarkUserScrollIntentStarted();
        _orchestrator.ClearAttachIntentOnly();
        _orchestrator.StopInitialScrollForManualInteraction();
        var command = viewState.IsAtBottom
            ? _orchestrator.Handle(_orchestrator.CreateUserIntentScrollEvent(_conversationId))
            : restoreToken is { } token
                ? _orchestrator.Handle(_orchestrator.CreateUserDetachedEvent(_conversationId, token))
                : _orchestrator.Handle(_orchestrator.CreateUserIntentScrollEvent(_conversationId));
        actions.AddRange(ApplyCommand(command));
        if (_orchestrator.HasPendingSettle)
        {
            actions.AddRange(ApplyScrollDecision(_orchestrator.AbortSettleForUserInteraction(), viewState));
        }

        return actions;
    }

    public void OnUserPointerPressed(bool isDetached)
    {
        if (isDetached || _orchestrator.IsViewportDetached)
        {
            _orchestrator.MarkDetachedViewportInteractionStarted();
            return;
        }

        _orchestrator.MarkUserScrollIntentStarted();
    }

    public void OnUserPointerReleased()
    {
        _orchestrator.MarkUserScrollIntentCompleted();
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnScheduledScrollObservation(
        TranscriptScrollRequestToken requestToken,
        TranscriptViewportViewState viewState)
    {
        if (!_orchestrator.MatchesActiveScrollRequest(requestToken, _conversationId))
        {
            return [];
        }

        return OnScheduledScrollObservationCore(viewState);
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnProjectionReady(
        string? conversationId,
        long projectionEpoch)
    {
        if (!_isLoaded
            || !_isSessionActive
            || string.IsNullOrWhiteSpace(conversationId))
        {
            return [];
        }

        return ApplyCommand(_orchestrator.Handle(
            _orchestrator.CreateProjectionReadyEvent(conversationId, projectionEpoch)));
    }

    public IReadOnlyList<TranscriptViewportControllerAction> SuspendForOverlay()
    {
        _isOverlayVisible = true;
        _orchestrator.ResetInteractionState();
        if (string.IsNullOrWhiteSpace(_conversationId))
        {
            return [];
        }

        return ApplyCommand(_orchestrator.InvalidateContext(_conversationId));
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnRestoreConfirmed(
        TranscriptProjectionRestoreToken token,
        int generation)
    {
        _orchestrator.ReleaseAutoScrollTracking();
        return ApplyCommand(_orchestrator.Handle(
            _orchestrator.CreateRestoreConfirmedEvent(token.ConversationId, generation, token)));
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnRestoreUnavailable(
        string? conversationId,
        int generation,
        string reason)
    {
        _orchestrator.ReleaseAutoScrollTracking();
        return ApplyCommand(_orchestrator.Handle(
            _orchestrator.CreateRestoreUnavailableEvent(
                string.IsNullOrWhiteSpace(conversationId) ? _conversationId : conversationId,
                generation,
                reason)));
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnRestoreAbandoned(
        string? conversationId,
        int generation,
        string reason)
    {
        _orchestrator.ReleaseAutoScrollTracking();
        return ApplyCommand(_orchestrator.Handle(
            _orchestrator.CreateRestoreAbandonedEvent(
                string.IsNullOrWhiteSpace(conversationId) ? _conversationId : conversationId,
                generation,
                reason)));
    }

    public bool TryCaptureActiveScrollRequest(out TranscriptScrollRequestToken token)
        => _orchestrator.TryCaptureActiveScrollRequestToken(_conversationId, out token);

    public bool MatchesActiveScrollRequest(TranscriptScrollRequestToken token)
        => _orchestrator.MatchesActiveScrollRequest(token, _conversationId);

    public IReadOnlyList<TranscriptViewportControllerAction> OnActiveScrollObservation(
        TranscriptViewportViewState viewState)
    {
        if (!TryCaptureActiveScrollRequest(out var token))
        {
            return [];
        }

        return OnScheduledScrollObservation(token, viewState);
    }

    private IReadOnlyList<TranscriptViewportControllerAction> OnScheduledScrollObservationCore(
        TranscriptViewportViewState viewState)
    {
        var observation = ResolveSettleObservation(viewState);
        var actions = ApplyScrollDecision(_orchestrator.ReportSettled(_conversationId, observation), viewState);
        if (actions.Count > 0)
        {
            return actions;
        }

        return [];
    }

    private IReadOnlyList<TranscriptViewportControllerAction> TryIssueScrollRequest(
        TranscriptViewportViewState viewState)
        => ApplyScrollDecision(
            _orchestrator.TryIssueScrollRequest(
                _conversationId,
                hasMessages: viewState.HasMessages,
                isReady: CanIssueScrollRequest(viewState)),
            viewState);

    private IReadOnlyList<TranscriptViewportControllerAction> ApplyScrollDecision(
        TranscriptScrollDecision decision,
        TranscriptViewportViewState viewState)
    {
        switch (decision.Action)
        {
            case TranscriptScrollAction.IssueScrollRequest:
                if (!_orchestrator.TryCaptureActiveScrollRequestToken(_conversationId, out var requestToken))
                {
                    return [];
                }

                return [new TranscriptViewportControllerAction(
                    TranscriptViewportControllerActionKind.ScrollLastMessageIntoView,
                    requestToken,
                    Generation: decision.Generation)];

            case TranscriptScrollAction.Completed:
            case TranscriptScrollAction.Aborted:
            case TranscriptScrollAction.Exhausted:
                _orchestrator.ReleaseAutoScrollTracking();
                _ = viewState;
                return [];

            default:
                return [];
        }
    }

    private IReadOnlyList<TranscriptViewportControllerAction> ObserveViewport(
        TranscriptViewportViewState viewState,
        TranscriptProjectionRestoreToken? restoreToken)
    {
        var fact = new TranscriptViewportFact(
            HasItems: viewState.HasMessages,
            IsReady: viewState.IsViewportReady,
            IsAtBottom: viewState.IsAtBottom,
            IsProgrammaticScrollInFlight: _orchestrator.IsProgrammaticScrollInFlight);
        return ApplyCommand(_orchestrator.ObserveViewportFact(_conversationId, fact, restoreToken).Command);
    }

    private IReadOnlyList<TranscriptViewportControllerAction> ApplyCommand(TranscriptViewportCommand command)
    {
        switch (command.Kind)
        {
            case TranscriptViewportCommandKind.IssueScrollToBottom:
                if (_orchestrator.TryBeginScrollToBottomSchedule(_conversationId, out var scheduleToken)
                    && _orchestrator.CanExecuteScrollToBottomSchedule(scheduleToken, _conversationId))
                {
                    _orchestrator.ReleaseScrollToBottomSchedule(scheduleToken);
                    return [new TranscriptViewportControllerAction(TranscriptViewportControllerActionKind.ScrollLastMessageIntoView)];
                }

                return [];

            case TranscriptViewportCommandKind.RequestRestore:
                if (command.RestoreToken is { } restoreToken)
                {
                    _orchestrator.MarkProjectionRestoreQueued();
                    return [new TranscriptViewportControllerAction(
                        TranscriptViewportControllerActionKind.RequestRestore,
                        RestoreToken: restoreToken,
                        Generation: command.Generation)];
                }

                return [];

            case TranscriptViewportCommandKind.StopProgrammaticScroll:
                _orchestrator.StopProgrammaticScroll();
                if (_orchestrator.HasPendingSettle)
                {
                    _ = _orchestrator.AbortSettleForUserInteraction();
                }

                return [new TranscriptViewportControllerAction(TranscriptViewportControllerActionKind.StopProgrammaticScroll)];

            case TranscriptViewportCommandKind.MarkAutoFollowDetached:
                _orchestrator.ClearAttachIntentOnly();
                _orchestrator.ClearScrollToBottomScheduled();
                return [new TranscriptViewportControllerAction(TranscriptViewportControllerActionKind.AutoFollowDetached)];

            case TranscriptViewportCommandKind.MarkAutoFollowAttached:
                _orchestrator.ClearAttachIntentOnly();
                return [new TranscriptViewportControllerAction(TranscriptViewportControllerActionKind.AutoFollowAttached)];

            default:
                return [];
        }
    }

    private IReadOnlyList<TranscriptViewportControllerAction> AbandonContext(string reason)
    {
        _ = reason;
        return [];
    }

    private bool CanIssueScrollRequest(TranscriptViewportViewState viewState)
        => CanTrackViewport(viewState.HasMessages)
            && viewState.IsViewReady
            && viewState.IsViewportReady;

    private bool CanObserveViewport(TranscriptViewportViewState viewState)
        => HasAuthoritativeConversationContext()
            && viewState.IsViewReady;

    private bool CanTrackViewport(bool hasMessages)
        => HasAuthoritativeConversationContext()
            && hasMessages;

    private bool HasAuthoritativeConversationContext()
        => _isLoaded
            && _isSessionActive
            && !_isOverlayVisible
            && !string.IsNullOrWhiteSpace(_conversationId);

    private static TranscriptScrollSettleObservation ResolveSettleObservation(
        TranscriptViewportViewState viewState)
    {
        if (!viewState.HasMessages || !viewState.IsViewportReady)
        {
            return TranscriptScrollSettleObservation.NotReadyYet;
        }

        return viewState.IsAtBottom && viewState.IsLastItemVisibleAtBottom
            ? TranscriptScrollSettleObservation.AtBottom
            : TranscriptScrollSettleObservation.ReadyButNotAtBottom;
    }

    private TranscriptViewportActivationKind ResolveInitialActivationKind(string conversationId)
    {
        var existing = string.IsNullOrWhiteSpace(conversationId)
            ? null
            : _orchestrator.GetConversationState(conversationId);

        return existing is null
            ? TranscriptViewportActivationKind.ColdEnter
            : TranscriptViewportActivationKind.WarmReturn;
    }

    private static string ResolveAuthoritativeConversationId(string? conversationId, bool isSessionActive)
        => isSessionActive && !string.IsNullOrWhiteSpace(conversationId)
            ? conversationId
            : string.Empty;

    private static TranscriptViewportViewState CreateViewStateFromAvailability(bool hasMessages)
        => new(
            IsViewReady: true,
            IsViewportReady: true,
            HasMessages: hasMessages,
            IsAtBottom: false,
            IsLastItemVisibleAtBottom: false);
}
