using System;
using System.Collections.Generic;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Utilities;

/// <summary>
/// Epoch-free follow controller facade preserving ChatView call signatures.
/// Intent: FollowingBottom vs PinnedToItem; context: conversationId + activation generation;
/// anchor: restorable item key. Native ListView executes scroll.
/// </summary>
public sealed class TranscriptViewportController
{
    private readonly TranscriptFollowController _follow = new();
    private string _conversationId = string.Empty;
    private bool _isLoaded;
    private bool _isSessionActive;
    private bool _isOverlayVisible;
    private bool _programmaticScrollInFlight;
    private bool _userScrollIntentPending;
    private int _activationGeneration;
    private TranscriptProjectionRestoreToken? _pinnedToken;
    private TranscriptScrollRequestToken _activeScrollToken;
    private bool _hasActiveScrollToken;

    public TranscriptViewportOrchestratorSnapshot Snapshot => new(
        State,
        IsAutoFollowAttached,
        IsViewportDetached,
        HasPendingSettle,
        IsProgrammaticScrollInFlight,
        AttachToBottomIntentPending,
        UserScrollIntentPending,
        UserScrollIntentCompleted,
        ScrollToEndScheduled: false,
        Generation,
        ScheduledScrollRequestVersion: 0,
        ActiveScrollGeneration: _hasActiveScrollToken ? _activeScrollToken.Generation : -1);

    public TranscriptViewportState State
        => !_isLoaded || string.IsNullOrWhiteSpace(_conversationId)
            ? TranscriptViewportState.Suspended
            : _follow.State.Mode switch
            {
                TranscriptFollowMode.FollowingBottom => TranscriptViewportState.Following,
                TranscriptFollowMode.PinnedToItem => TranscriptViewportState.DetachedByUser,
                _ => TranscriptViewportState.Suspended
            };

    public bool IsViewportDetached => _follow.IsPinned;
    public bool IsAutoFollowAttached => _follow.IsFollowingBottom;
    public bool HasPendingSettle => false;
    public bool HasActiveScrollGeneration => _hasActiveScrollToken;
    public bool IsProgrammaticScrollInFlight => _programmaticScrollInFlight;
    public bool AttachToBottomIntentPending => false;
    public bool UserScrollIntentPending => _userScrollIntentPending;
    public bool UserScrollIntentCompleted => !_userScrollIntentPending;
    public int Generation => _activationGeneration;
    public TranscriptViewportTransition? LastTransition => null;

    public TranscriptViewportConversationState? GetConversationState(string conversationId)
    {
        if (!string.Equals(_conversationId, conversationId, StringComparison.Ordinal))
        {
            return null;
        }

        return new TranscriptViewportConversationState(
            State,
            Anchor: null,
            LastKnownBottomState: _follow.IsFollowingBottom,
            LastActivationGeneration: _activationGeneration,
            RestorePending: false,
            RestoreToken: _pinnedToken);
    }

    public void MarkProjectionRestoreQueued() { }
    public void MarkDetachedViewportInteractionStarted() { }
    public void MarkUserScrollIntentStarted() => _userScrollIntentPending = true;
    public void MarkUserScrollIntentCompleted() => _userScrollIntentPending = false;

    public void Load(string? conversationId, bool isSessionActive, bool isOverlayVisible, bool hasMessages)
    {
        _isLoaded = true;
        _isSessionActive = isSessionActive;
        _isOverlayVisible = isOverlayVisible;
        _conversationId = ResolveConversationId(conversationId, isSessionActive);
        _userScrollIntentPending = false;
        _programmaticScrollInFlight = false;
        _hasActiveScrollToken = false;
        if (!_isOverlayVisible && isSessionActive && !string.IsNullOrWhiteSpace(_conversationId))
        {
            _activationGeneration++;
            _ = _follow.Activate(_conversationId, _activationGeneration);
        }
        else
        {
            _follow.Deactivate();
        }
    }

    public IReadOnlyList<TranscriptViewportControllerAction> Unload()
    {
        _isLoaded = false;
        _follow.Deactivate();
        _conversationId = string.Empty;
        _isSessionActive = false;
        _isOverlayVisible = false;
        _hasActiveScrollToken = false;
        _pinnedToken = null;
        return [];
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnConversationChanged(
        string? conversationId,
        bool isSessionActive,
        bool isOverlayVisible,
        bool hasMessages)
    {
        _isSessionActive = isSessionActive;
        _isOverlayVisible = isOverlayVisible;
        _conversationId = ResolveConversationId(conversationId, isSessionActive);
        _userScrollIntentPending = false;
        if (string.IsNullOrWhiteSpace(_conversationId) || isOverlayVisible || !isSessionActive)
        {
            _follow.Deactivate();
            return [];
        }

        _activationGeneration++;
        return ToActions(_follow.Activate(_conversationId, _activationGeneration), hasMessages);
    }

    public IReadOnlyList<TranscriptViewportControllerAction> ActivateCurrentConversation(
        string? conversationId,
        bool isSessionActive,
        bool isOverlayVisible,
        bool hasMessages,
        TranscriptViewportActivationKind activationKind)
        => OnConversationChanged(conversationId, isSessionActive, isOverlayVisible, hasMessages);

    public IReadOnlyList<TranscriptViewportControllerAction> OnMessagesAppended(
        int addedCount,
        TranscriptViewportViewState viewState)
    {
        if (!_isLoaded || string.IsNullOrWhiteSpace(_conversationId))
        {
            return [];
        }

        return ContentChanged(pinStillResolvable: true, pinIsVisible: true);
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnViewportChanged(
        TranscriptViewportViewState viewState,
        TranscriptProjectionRestoreToken? restoreToken = null)
    {
        if (!_isLoaded || string.IsNullOrWhiteSpace(_conversationId))
        {
            return [];
        }

        var topKey = restoreToken?.ProjectionItemKey;
        var request = _follow.Observe(new TranscriptViewportObservation(
            _conversationId,
            _activationGeneration,
            viewState.HasMessages,
            viewState.IsAtBottom,
            _programmaticScrollInFlight,
            topKey));

        if (_follow.IsPinned && restoreToken is { } token)
        {
            _pinnedToken = token;
        }
        else if (_follow.IsFollowingBottom)
        {
            _pinnedToken = null;
        }

        return ToActions(request, viewState.HasMessages);
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnUserViewportIntent(
        TranscriptViewportViewState viewState,
        TranscriptProjectionRestoreToken? restoreToken = null)
        => OnViewportChanged(viewState, restoreToken);

    public IReadOnlyList<TranscriptViewportControllerAction> OnUserViewportDetachIntent(
        TranscriptViewportViewState viewState,
        TranscriptProjectionRestoreToken? restoreToken = null)
        => OnViewportChanged(viewState, restoreToken);

    public void OnUserPointerPressed(bool isDetached)
        => _userScrollIntentPending = true;

    public void OnUserPointerReleased()
        => _userScrollIntentPending = false;

    public IReadOnlyList<TranscriptViewportControllerAction> OnScheduledScrollObservation(
        TranscriptScrollRequestToken requestToken,
        TranscriptViewportViewState viewState)
    {
        if (!MatchesActiveScrollRequest(requestToken))
        {
            return [];
        }

        _programmaticScrollInFlight = false;
        _hasActiveScrollToken = false;
        return [];
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnProjectionReady(
        string? conversationId,
        long projectionEpochIgnored = 0)
    {
        if (!_isLoaded || !_isSessionActive || string.IsNullOrWhiteSpace(conversationId))
        {
            return [];
        }

        if (!string.Equals(_conversationId, conversationId, StringComparison.Ordinal))
        {
            return [];
        }

        // Content changed: if pinned and not visible, re-anchor; if following, scroll end.
        return ContentChanged(pinStillResolvable: _pinnedToken is not null, pinIsVisible: false);
    }

    public IReadOnlyList<TranscriptViewportControllerAction> SuspendForOverlay()
    {
        _isOverlayVisible = true;
        _follow.Deactivate();
        _hasActiveScrollToken = false;
        return [];
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnRestoreConfirmed(
        TranscriptProjectionRestoreToken token,
        int generation)
    {
        _programmaticScrollInFlight = false;
        return [];
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnRestoreUnavailable(
        string? conversationId,
        int generation,
        string reason)
        => ContentChanged(pinStillResolvable: false, pinIsVisible: false);

    public IReadOnlyList<TranscriptViewportControllerAction> OnRestoreAbandoned(
        string? conversationId,
        int generation,
        string reason)
    {
        _hasActiveScrollToken = false;
        return [];
    }

    public bool TryCaptureActiveScrollRequest(out TranscriptScrollRequestToken token)
    {
        if (_hasActiveScrollToken)
        {
            token = _activeScrollToken;
            return true;
        }

        token = default;
        return false;
    }

    public bool MatchesActiveScrollRequest(TranscriptScrollRequestToken token)
        => _hasActiveScrollToken
           && token.Generation == _activeScrollToken.Generation
           && string.Equals(token.ConversationId, _activeScrollToken.ConversationId, StringComparison.Ordinal);

    public IReadOnlyList<TranscriptViewportControllerAction> OnActiveScrollObservation(
        TranscriptViewportViewState viewState)
    {
        if (!TryCaptureActiveScrollRequest(out var token))
        {
            return [];
        }

        return OnScheduledScrollObservation(token, viewState);
    }

    private IReadOnlyList<TranscriptViewportControllerAction> ContentChanged(
        bool pinStillResolvable,
        bool pinIsVisible)
    {
        if (string.IsNullOrWhiteSpace(_conversationId))
        {
            return [];
        }

        if (_follow.IsPinned && _pinnedToken is { } pin)
        {
            pinStillResolvable = TranscriptItemKey.IsRestorable(pin.ProjectionItemKey) && pinStillResolvable;
        }

        return ToActions(
            _follow.OnContentChanged(_conversationId, _activationGeneration, pinStillResolvable, pinIsVisible),
            hasMessages: true);
    }

    private IReadOnlyList<TranscriptViewportControllerAction> ToActions(
        TranscriptScrollRequest request,
        bool hasMessages)
    {
        if (request.Kind == TranscriptScrollRequestKind.None || !hasMessages)
        {
            return MapModeSideEffects(request);
        }

        var token = new TranscriptScrollRequestToken(_activationGeneration, _conversationId);
        _activeScrollToken = token;
        _hasActiveScrollToken = true;
        _programmaticScrollInFlight = true;

        var kind = request.Kind == TranscriptScrollRequestKind.ScrollIntoView
            ? TranscriptViewportControllerActionKind.ScrollIntoView
            : TranscriptViewportControllerActionKind.ScrollTranscriptToEnd;

        var actions = new List<TranscriptViewportControllerAction>
        {
            new(kind, token, _pinnedToken, _activationGeneration, request.ItemKey)
        };
        actions.AddRange(MapModeSideEffects(request));
        return actions;
    }

    private IReadOnlyList<TranscriptViewportControllerAction> MapModeSideEffects(
        TranscriptScrollRequest request)
    {
        // Emit follow mode markers for automation/logging paths that switch on them.
        if (_follow.IsPinned)
        {
            return
            [
                new TranscriptViewportControllerAction(
                    TranscriptViewportControllerActionKind.AutoFollowDetached,
                    Generation: _activationGeneration,
                    RestoreToken: _pinnedToken)
            ];
        }

        if (_follow.IsFollowingBottom)
        {
            return
            [
                new TranscriptViewportControllerAction(
                    TranscriptViewportControllerActionKind.AutoFollowAttached,
                    Generation: _activationGeneration)
            ];
        }

        return [];
    }

    private static string ResolveConversationId(string? conversationId, bool isSessionActive)
        => isSessionActive && !string.IsNullOrWhiteSpace(conversationId)
            ? conversationId
            : string.Empty;
}
