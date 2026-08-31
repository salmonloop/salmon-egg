using System;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Utilities;

/// <summary>
/// Single owner of transcript follow intent.
/// Dimensions: intent (mode), anchor (item key), context (conversation + generation).
/// No content-version / ProjectionEpoch clock.
/// </summary>
public sealed class TranscriptFollowController
{
    private TranscriptFollowState _state = new(
        ConversationId: null,
        ActivationGeneration: 0,
        Mode: TranscriptFollowMode.Suspended,
        PinnedItemKey: null);

    public TranscriptFollowState State => _state;

    public bool IsFollowingBottom
        => _state.Mode == TranscriptFollowMode.FollowingBottom;

    public bool IsPinned
        => _state.Mode == TranscriptFollowMode.PinnedToItem;

    public TranscriptScrollRequest Activate(string conversationId, int activationGeneration)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            Deactivate();
            return None("ActivateWithoutConversation");
        }

        _state = new TranscriptFollowState(
            conversationId,
            activationGeneration,
            TranscriptFollowMode.FollowingBottom,
            PinnedItemKey: null);

        return ScrollToEnd(conversationId, activationGeneration, "ActivateFollowBottom");
    }

    public void ActivatePinned(
        string conversationId,
        int activationGeneration,
        string pinnedItemKey)
    {
        if (string.IsNullOrWhiteSpace(conversationId)
            || !TranscriptItemKey.IsRestorable(pinnedItemKey))
        {
            Deactivate();
            return;
        }

        _state = new TranscriptFollowState(
            conversationId,
            activationGeneration,
            TranscriptFollowMode.PinnedToItem,
            pinnedItemKey);
    }

    public void Deactivate()
    {
        _state = new TranscriptFollowState(
            ConversationId: null,
            ActivationGeneration: 0,
            Mode: TranscriptFollowMode.Suspended,
            PinnedItemKey: null);
    }

    public TranscriptScrollRequest JumpToLatest()
    {
        if (_state.Mode == TranscriptFollowMode.Suspended
            || string.IsNullOrWhiteSpace(_state.ConversationId))
        {
            return None("JumpWhileSuspended");
        }

        _state = _state with
        {
            Mode = TranscriptFollowMode.FollowingBottom,
            PinnedItemKey = null
        };

        return ScrollToEnd(_state.ConversationId, _state.ActivationGeneration, "JumpToLatest");
    }

    public TranscriptScrollRequest Observe(TranscriptViewportObservation observation)
    {
        if (!MatchesContext(observation.ConversationId, observation.ActivationGeneration))
        {
            return None("ObserveStaleContext");
        }

        if (_state.Mode == TranscriptFollowMode.Suspended)
        {
            return None("ObserveWhileSuspended");
        }

        if (!observation.HasItems)
        {
            _state = _state with
            {
                Mode = TranscriptFollowMode.FollowingBottom,
                PinnedItemKey = null
            };
            return None("ObserveEmptyTranscript");
        }

        if (observation.ProgrammaticScrollInFlight)
        {
            return None("ObserveProgrammaticScroll");
        }

        if (observation.IsAtBottom)
        {
            if (_state.Mode != TranscriptFollowMode.FollowingBottom)
            {
                _state = _state with
                {
                    Mode = TranscriptFollowMode.FollowingBottom,
                    PinnedItemKey = null
                };
            }

            return None("ObserveAtBottom");
        }

        if (TranscriptItemKey.IsRestorable(observation.TopVisibleItemKey))
        {
            _state = _state with
            {
                Mode = TranscriptFollowMode.PinnedToItem,
                PinnedItemKey = observation.TopVisibleItemKey
            };
            return None("ObservePinnedToVisibleItem");
        }

        return None("ObserveOffBottomWithoutRestorableKey");
    }

    public TranscriptScrollRequest OnContentChanged(
        string conversationId,
        int activationGeneration,
        bool pinStillResolvable,
        bool pinIsVisible = true)
    {
        if (!MatchesContext(conversationId, activationGeneration))
        {
            return None("ContentChangedStaleContext");
        }

        return _state.Mode switch
        {
            TranscriptFollowMode.FollowingBottom
                => ScrollToEnd(conversationId, activationGeneration, "ContentChangedFollowBottom"),

            TranscriptFollowMode.PinnedToItem when !pinStillResolvable
                => FallBackToBottom(conversationId, activationGeneration, "PinnedItemMissing"),

            TranscriptFollowMode.PinnedToItem when !pinIsVisible
                && TranscriptItemKey.IsRestorable(_state.PinnedItemKey)
                => ScrollIntoView(
                    conversationId,
                    activationGeneration,
                    _state.PinnedItemKey!,
                    "ContentChangedReanchorPin"),

            TranscriptFollowMode.PinnedToItem
                => None("ContentChangedPinStable"),

            _
                => None("ContentChangedSuspended")
        };
    }

    private TranscriptScrollRequest FallBackToBottom(
        string conversationId,
        int activationGeneration,
        string reason)
    {
        _state = _state with
        {
            Mode = TranscriptFollowMode.FollowingBottom,
            PinnedItemKey = null
        };
        return ScrollToEnd(conversationId, activationGeneration, reason);
    }

    private bool MatchesContext(string conversationId, int activationGeneration)
        => _state.Mode != TranscriptFollowMode.Suspended
           && !string.IsNullOrWhiteSpace(_state.ConversationId)
           && string.Equals(_state.ConversationId, conversationId, StringComparison.Ordinal)
           && _state.ActivationGeneration == activationGeneration;

    private static TranscriptScrollRequest ScrollToEnd(
        string conversationId,
        int activationGeneration,
        string reason)
        => new(
            TranscriptScrollRequestKind.ScrollToEnd,
            conversationId,
            activationGeneration,
            ItemKey: null,
            reason);

    private static TranscriptScrollRequest ScrollIntoView(
        string conversationId,
        int activationGeneration,
        string itemKey,
        string reason)
        => new(
            TranscriptScrollRequestKind.ScrollIntoView,
            conversationId,
            activationGeneration,
            itemKey,
            reason);

    private static TranscriptScrollRequest None(string reason)
        => new(TranscriptScrollRequestKind.None, Reason: reason);
}
