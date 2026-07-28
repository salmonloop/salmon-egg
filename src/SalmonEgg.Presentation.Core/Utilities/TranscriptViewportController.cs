using System;
using System.Collections.Generic;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Utilities;

/// <summary>
/// Single owner of transcript follow state across conversation switches.
/// Intent stays in Core; native ListView remains responsible for the actual scroll operation.
/// </summary>
public sealed class TranscriptViewportController
{
    private readonly TranscriptFollowController _follow = new();
    private readonly Dictionary<string, TranscriptViewportConversationState> _conversationStates = new(StringComparer.Ordinal);
    private string _conversationId = string.Empty;
    private bool _isLoaded;
    private bool _isSessionActive;
    private bool _isOverlayVisible;
    private bool _overlayResumePending;
    private bool _hasLoadedOnce;
    private TranscriptViewportActivationKind? _pendingLoadActivationKind;
    private bool _programmaticScrollInFlight;
    private bool _userScrollIntentPending;
    private int _activationGeneration;
    private long _scrollRequestGeneration;
    private TranscriptProjectionRestoreToken? _pinnedToken;
    private TranscriptScrollRequestToken _activeScrollToken;
    private bool _hasActiveScrollToken;

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
    public bool UserScrollIntentPending => _userScrollIntentPending;
    public bool UserScrollIntentCompleted => !_userScrollIntentPending;
    public int Generation => _activationGeneration;

    public TranscriptViewportConversationState? GetConversationState(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        if (string.Equals(_conversationId, conversationId, StringComparison.Ordinal)
            && State != TranscriptViewportState.Suspended)
        {
            return CreateCurrentConversationState();
        }

        return _conversationStates.TryGetValue(conversationId, out var state)
            ? state
            : null;
    }

    public void MarkUserScrollIntentStarted() => _userScrollIntentPending = true;

    public void MarkUserScrollIntentCompleted() => _userScrollIntentPending = false;

    public void Load(
        string? conversationId,
        bool isSessionActive,
        bool isOverlayVisible)
    {
        PersistCurrentConversationState();
        _isLoaded = true;
        _isSessionActive = isSessionActive;
        _isOverlayVisible = isOverlayVisible;
        _overlayResumePending = isOverlayVisible;
        _pendingLoadActivationKind = _hasLoadedOnce
            ? TranscriptViewportActivationKind.WarmReturn
            : TranscriptViewportActivationKind.ColdEnter;
        _hasLoadedOnce = true;
        ClearTransientScrollState();
        _userScrollIntentPending = false;
        _conversationId = ResolveConversationId(conversationId, isSessionActive);
        _follow.Deactivate();
        _pinnedToken = null;
    }

    public bool TryActivateAfterLoad(
        string? conversationId,
        bool isSessionActive,
        bool isOverlayVisible,
        bool hasMessages,
        out IReadOnlyList<TranscriptViewportControllerAction> actions)
    {
        actions = [];
        if (!_isLoaded || _pendingLoadActivationKind is not { } activationKind)
        {
            return false;
        }

        SynchronizePendingLoadContext(conversationId, isSessionActive, isOverlayVisible);
        if (!CanActivateCurrentConversation() || _overlayResumePending)
        {
            return false;
        }

        _pendingLoadActivationKind = null;
        actions = ActivateResolvedConversation(activationKind, hasMessages);
        return true;
    }

    public IReadOnlyList<TranscriptViewportControllerAction> Unload()
    {
        PersistCurrentConversationState();
        _isLoaded = false;
        _follow.Deactivate();
        _conversationId = string.Empty;
        _isSessionActive = false;
        _isOverlayVisible = false;
        _overlayResumePending = false;
        _pendingLoadActivationKind = null;
        ClearTransientScrollState();
        _userScrollIntentPending = false;
        _pinnedToken = null;
        return [];
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnConversationChanged(
        string? conversationId,
        bool isSessionActive,
        bool isOverlayVisible,
        bool hasMessages)
    {
        if (_pendingLoadActivationKind is not null)
        {
            SynchronizePendingLoadContext(conversationId, isSessionActive, isOverlayVisible);
            return [];
        }

        return ActivateConversation(
            conversationId,
            isSessionActive,
            isOverlayVisible,
            hasMessages,
            TranscriptViewportActivationKind.WarmReturn);
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnOverlayVisibilityChanged(
        bool isOverlayVisible)
    {
        if (_isOverlayVisible == isOverlayVisible)
        {
            return [];
        }

        if (isOverlayVisible)
        {
            PersistCurrentConversationState();
        }

        _isOverlayVisible = isOverlayVisible;
        _overlayResumePending = true;
        _follow.Deactivate();
        ClearTransientScrollState();
        _userScrollIntentPending = false;
        _pinnedToken = null;
        return [];
    }

    public bool TryResumeAfterOverlay(
        string? conversationId,
        bool isSessionActive,
        bool isOverlayVisible,
        bool hasMessages,
        out IReadOnlyList<TranscriptViewportControllerAction> actions)
    {
        actions = [];
        if (!_overlayResumePending
            || !_isLoaded
            || isOverlayVisible
            || !isSessionActive
            || string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        actions = ActivateConversation(
            conversationId,
            isSessionActive,
            isOverlayVisible,
            hasMessages,
            TranscriptViewportActivationKind.OverlayResume,
            consumeOverlayResume: true);
        _pendingLoadActivationKind = null;
        return true;
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnTranscriptContentChanged(
        TranscriptViewportViewState viewState)
    {
        if (!_isLoaded || string.IsNullOrWhiteSpace(_conversationId))
        {
            return [];
        }

        return ContentChanged(pinStillResolvable: true, pinIsVisible: true, viewState.HasMessages);
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnViewportChanged(
        TranscriptViewportViewState viewState,
        TranscriptProjectionRestoreToken? restoreToken = null)
        => ObserveViewport(viewState, restoreToken, isUserGesture: false);

    public IReadOnlyList<TranscriptViewportControllerAction> OnUserViewportIntent(
        TranscriptViewportViewState viewState,
        TranscriptProjectionRestoreToken? restoreToken = null)
        => ObserveViewport(viewState, restoreToken, isUserGesture: true);

    public IReadOnlyList<TranscriptViewportControllerAction> OnUserViewportDetachIntent(
        TranscriptViewportViewState viewState,
        TranscriptProjectionRestoreToken? restoreToken = null)
        => ObserveViewport(viewState, restoreToken, isUserGesture: true);

    private IReadOnlyList<TranscriptViewportControllerAction> OnScheduledScrollObservation(
        TranscriptScrollRequestToken requestToken)
    {
        if (!MatchesActiveScrollRequest(requestToken))
        {
            return [];
        }

        _programmaticScrollInFlight = false;
        _hasActiveScrollToken = false;
        PersistCurrentConversationState();
        return [];
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnProjectionReady(
        string? conversationId)
    {
        if (!_isLoaded || !_isSessionActive || string.IsNullOrWhiteSpace(conversationId))
        {
            return [];
        }

        if (!string.Equals(_conversationId, conversationId, StringComparison.Ordinal))
        {
            return [];
        }

        var pinResolvable = _pinnedToken is { } pin
            && TranscriptItemKey.IsRestorable(pin.ProjectionItemKey);
        return ContentChanged(
            pinStillResolvable: pinResolvable || !_follow.IsPinned,
            pinIsVisible: true,
            hasMessages: true);
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnRestoreConfirmed(
        TranscriptProjectionRestoreToken token,
        int generation)
    {
        if (MatchesRestoreContext(token.ConversationId, generation))
        {
            _programmaticScrollInFlight = false;
            _hasActiveScrollToken = false;
            PersistCurrentConversationState();
        }

        return [];
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnRestoreUnavailable(
        string? conversationId,
        int generation,
        bool hasMessages)
    {
        if (!MatchesRestoreContext(conversationId, generation))
        {
            return [];
        }

        var actions = ContentChanged(pinStillResolvable: false, pinIsVisible: false, hasMessages);
        PersistCurrentConversationState();
        return actions;
    }

    public IReadOnlyList<TranscriptViewportControllerAction> OnRestoreAbandoned(
        string? conversationId,
        int generation)
    {
        if (!MatchesRestoreContext(conversationId, generation))
        {
            return [];
        }

        ClearTransientScrollState();
        PersistCurrentConversationState();
        return [];
    }

    public bool MatchesActiveScrollRequest(TranscriptScrollRequestToken token)
        => _hasActiveScrollToken && token == _activeScrollToken;

    public IReadOnlyList<TranscriptViewportControllerAction> OnActiveScrollObservation(
        TranscriptScrollRequestToken requestToken)
        => OnScheduledScrollObservation(requestToken);

    private IReadOnlyList<TranscriptViewportControllerAction> ActivateConversation(
        string? conversationId,
        bool isSessionActive,
        bool isOverlayVisible,
        bool hasMessages,
        TranscriptViewportActivationKind activationKind,
        bool consumeOverlayResume = false)
    {
        var overlayVisibilityChanged = _isOverlayVisible != isOverlayVisible;
        PersistCurrentConversationState();
        _isSessionActive = isSessionActive;
        _isOverlayVisible = isOverlayVisible;
        _conversationId = ResolveConversationId(conversationId, isSessionActive);
        ClearTransientScrollState();
        _userScrollIntentPending = false;

        if (isOverlayVisible || overlayVisibilityChanged)
        {
            _overlayResumePending = true;
        }

        if (!CanActivateCurrentConversation()
            || (_overlayResumePending && !consumeOverlayResume))
        {
            _follow.Deactivate();
            _pinnedToken = null;
            return [];
        }

        if (consumeOverlayResume)
        {
            _overlayResumePending = false;
        }

        return ActivateResolvedConversation(activationKind, hasMessages);
    }

    private void SynchronizePendingLoadContext(
        string? conversationId,
        bool isSessionActive,
        bool isOverlayVisible)
    {
        var overlayVisibilityChanged = _isOverlayVisible != isOverlayVisible;
        _isSessionActive = isSessionActive;
        _isOverlayVisible = isOverlayVisible;
        _conversationId = ResolveConversationId(conversationId, isSessionActive);
        ClearTransientScrollState();
        _userScrollIntentPending = false;

        if (isOverlayVisible || overlayVisibilityChanged)
        {
            _overlayResumePending = true;
        }

        _follow.Deactivate();
        _pinnedToken = null;
    }

    private IReadOnlyList<TranscriptViewportControllerAction> ActivateResolvedConversation(
        TranscriptViewportActivationKind activationKind,
        bool hasMessages)
    {
        _activationGeneration++;
        if (activationKind == TranscriptViewportActivationKind.ColdEnter)
        {
            _conversationStates.Remove(_conversationId);
        }

        if (ShouldRestoreStoredPin(activationKind)
            && _conversationStates.TryGetValue(_conversationId, out var storedState)
            && storedState.RestoreToken is { } restoreToken
            && string.Equals(restoreToken.ConversationId, _conversationId, StringComparison.Ordinal)
            && TranscriptItemKey.IsRestorable(restoreToken.ProjectionItemKey))
        {
            _pinnedToken = restoreToken;
            _follow.ActivatePinned(
                _conversationId,
                _activationGeneration,
                restoreToken.ProjectionItemKey);
            _programmaticScrollInFlight = true;
            _hasActiveScrollToken = false;
            var actions = new List<TranscriptViewportControllerAction>
            {
                new(
                    TranscriptViewportControllerActionKind.RequestRestore,
                    RestoreToken: restoreToken,
                    Generation: _activationGeneration)
            };
            PersistCurrentConversationState();
            return actions;
        }

        _pinnedToken = null;
        var request = _follow.Activate(_conversationId, _activationGeneration);
        var result = ToActions(request, hasMessages);
        PersistCurrentConversationState();
        return result;
    }

    private IReadOnlyList<TranscriptViewportControllerAction> ObserveViewport(
        TranscriptViewportViewState viewState,
        TranscriptProjectionRestoreToken? restoreToken,
        bool isUserGesture)
    {
        if (!_isLoaded || string.IsNullOrWhiteSpace(_conversationId))
        {
            return [];
        }

        if (isUserGesture)
        {
            _userScrollIntentPending = true;
            ClearTransientScrollState();
        }

        var mayPin =
            isUserGesture
            || _follow.IsPinned
            || _userScrollIntentPending
            || restoreToken is { ProjectionItemKey: { Length: > 0 } };

        if (!viewState.IsAtBottom && !mayPin)
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

        if (_follow.IsPinned)
        {
            if (restoreToken is { } token
                && string.Equals(token.ConversationId, _conversationId, StringComparison.Ordinal))
            {
                _pinnedToken = token;
            }
            else if (TranscriptItemKey.IsRestorable(topKey))
            {
                _pinnedToken = new TranscriptProjectionRestoreToken(_conversationId, topKey!);
            }
        }
        else if (_follow.IsFollowingBottom)
        {
            _pinnedToken = null;
        }

        var actions = ToActions(request, viewState.HasMessages);
        PersistCurrentConversationState();
        return actions;
    }

    private IReadOnlyList<TranscriptViewportControllerAction> ContentChanged(
        bool pinStillResolvable,
        bool pinIsVisible,
        bool hasMessages)
    {
        if (string.IsNullOrWhiteSpace(_conversationId))
        {
            return [];
        }

        if (_follow.IsPinned && _pinnedToken is { } pin)
        {
            pinStillResolvable = TranscriptItemKey.IsRestorable(pin.ProjectionItemKey) && pinStillResolvable;
        }

        var actions = ToActions(
            _follow.OnContentChanged(_conversationId, _activationGeneration, pinStillResolvable, pinIsVisible),
            hasMessages);

        if (_follow.IsFollowingBottom)
        {
            _pinnedToken = null;
        }

        PersistCurrentConversationState();
        return actions;
    }

    private IReadOnlyList<TranscriptViewportControllerAction> ToActions(
        TranscriptScrollRequest request,
        bool hasMessages)
    {
        if (request.Kind == TranscriptScrollRequestKind.None || !hasMessages)
        {
            return [];
        }

        var token = new TranscriptScrollRequestToken(
            _activationGeneration,
            ++_scrollRequestGeneration,
            _conversationId);
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
        return actions;
    }

    private void PersistCurrentConversationState()
    {
        if (string.IsNullOrWhiteSpace(_conversationId))
        {
            return;
        }

        var state = CreateCurrentConversationState();
        if (state.Mode == TranscriptViewportState.Suspended)
        {
            return;
        }

        _conversationStates[_conversationId] = state;
    }

    private TranscriptViewportConversationState CreateCurrentConversationState()
    {
        var restoreToken = _follow.IsPinned ? _pinnedToken : null;
        return new TranscriptViewportConversationState(
            State,
            RestoreToken: restoreToken);
    }

    private void ClearTransientScrollState()
    {
        _programmaticScrollInFlight = false;
        _hasActiveScrollToken = false;
    }

    private bool CanActivateCurrentConversation()
        => _isLoaded
           && _isSessionActive
           && !_isOverlayVisible
           && !string.IsNullOrWhiteSpace(_conversationId);

    private bool MatchesRestoreContext(string? conversationId, int generation)
        => !string.IsNullOrWhiteSpace(conversationId)
           && string.Equals(conversationId, _conversationId, StringComparison.Ordinal)
           && generation == _activationGeneration;

    private static bool ShouldRestoreStoredPin(TranscriptViewportActivationKind activationKind)
        => activationKind is TranscriptViewportActivationKind.WarmReturn
            or TranscriptViewportActivationKind.OverlayResume;

    private static string ResolveConversationId(string? conversationId, bool isSessionActive)
        => isSessionActive && !string.IsNullOrWhiteSpace(conversationId)
            ? conversationId
            : string.Empty;
}
