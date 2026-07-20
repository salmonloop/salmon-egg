using SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Overlay;

public sealed class ChatConversationSurfaceStatePresenterTests
{
    [Fact]
    public void Resolve_WhenOnlyLayoutSettlingIsActive_DoesNotSurfaceActivationPresenter()
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(new ChatConversationSurfaceStateInput(
            IsSessionActive: false,
            CurrentSessionId: null,
            MessageHistoryCount: 0,
            VisibleTranscriptConversationId: null,
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: false,
            IsInitializing: false,
            IsHydrating: false,
            IsLayoutLoading: true,
            IsSessionSwitching: false,
            SessionSwitchOverlayConversationId: null,
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: null,
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 0));

        Assert.False(state.IsActivationOverlayVisible);
        Assert.True(state.IsOverlayVisible);
        Assert.False(state.ShouldShowBlockingLoadingMask);
        Assert.False(state.ShouldShowLoadingOverlayStatusPill);
        Assert.False(state.ShouldShowLoadingOverlayPresenter);
        Assert.Equal(string.Empty, state.OverlayStatusText);
    }

    [Fact]
    public void Resolve_WhenHydratingHistory_UsesUserFriendlyLoadedCountStatus()
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-1",
            MessageHistoryCount: 1,
            VisibleTranscriptConversationId: "conv-1",
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: false,
            IsInitializing: false,
            IsHydrating: true,
            IsLayoutLoading: false,
            IsSessionSwitching: false,
            SessionSwitchOverlayConversationId: null,
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: null,
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 2));

        Assert.Equal(ChatViewModel.LoadingOverlayStage.HydratingHistory, state.OverlayLoadingStage);
        Assert.Contains("Loading chat history", state.OverlayStatusText, StringComparison.Ordinal);
        Assert.Contains("2 messages loaded", state.OverlayStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenHydratingHistoryForCurrentConversation_HidesTranscriptUntilHydrationCompletes()
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-1",
            MessageHistoryCount: 1,
            VisibleTranscriptConversationId: "conv-1",
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: false,
            IsInitializing: false,
            IsHydrating: true,
            IsLayoutLoading: false,
            IsSessionSwitching: false,
            SessionSwitchOverlayConversationId: null,
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: null,
            HistoryOverlayConversationId: "conv-1",
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 1));

        Assert.True(state.IsActivationOverlayVisible);
        Assert.True(state.ShouldShowBlockingLoadingMask);
        Assert.True(state.ShouldShowLoadingOverlayPresenter);
        Assert.False(state.ShouldShowActiveConversationRoot);
        Assert.False(state.ShouldShowSessionHeader);
        Assert.False(state.ShouldShowTranscriptSurface);
        Assert.False(state.ShouldShowConversationInputSurface);
        Assert.Equal(ChatViewModel.LoadingOverlayStage.HydratingHistory, state.OverlayLoadingStage);
    }

    [Fact]
    public void Resolve_WhenRemoteSelectionOwnsCurrentConversationWithCachedTranscript_HidesTranscriptUntilAuthoritativeHydration()
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-1",
            MessageHistoryCount: 1,
            VisibleTranscriptConversationId: "conv-1",
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: false,
            IsInitializing: false,
            IsHydrating: false,
            IsLayoutLoading: false,
            IsSessionSwitching: true,
            SessionSwitchOverlayConversationId: "conv-1",
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: null,
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 0));

        Assert.True(state.IsActivationOverlayVisible);
        Assert.True(state.ShouldShowBlockingLoadingMask);
        Assert.True(state.ShouldShowLoadingOverlayPresenter);
        Assert.False(state.ShouldShowActiveConversationRoot);
        Assert.False(state.ShouldShowSessionHeader);
        Assert.False(state.ShouldShowTranscriptSurface);
        Assert.False(state.ShouldShowConversationInputSurface);
        Assert.Equal(ChatViewModel.LoadingOverlayStage.PreparingSession, state.OverlayLoadingStage);
    }

    [Fact]
    public void Resolve_WhenHydratingWithStaleVisibleTranscript_HidesConversationChrome()
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-2",
            MessageHistoryCount: 1,
            VisibleTranscriptConversationId: "conv-1",
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: false,
            IsInitializing: false,
            IsHydrating: true,
            IsLayoutLoading: false,
            IsSessionSwitching: false,
            SessionSwitchOverlayConversationId: null,
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: null,
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 0));

        Assert.True(state.IsActivationOverlayVisible);
        Assert.True(state.ShouldShowBlockingLoadingMask);
        Assert.True(state.ShouldShowLoadingOverlayPresenter);
        Assert.False(state.ShouldShowActiveConversationRoot);
        Assert.False(state.ShouldShowSessionHeader);
        Assert.False(state.ShouldShowTranscriptSurface);
        Assert.False(state.ShouldShowConversationInputSurface);
    }

    [Fact]
    public void Resolve_WhenPreviewingDifferentConversationWithVisibleTranscript_ShowsBlockingMask()
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-1",
            MessageHistoryCount: 1,
            VisibleTranscriptConversationId: "conv-1",
            IsChatShellVisibleForRemoteUi: false,
            IsConnecting: false,
            IsInitializing: false,
            IsHydrating: false,
            IsLayoutLoading: false,
            IsSessionSwitching: false,
            SessionSwitchOverlayConversationId: null,
            SessionSwitchPreviewConversationId: "conv-2",
            ConnectionLifecycleOverlayConversationId: null,
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 0));

        Assert.True(state.IsActivationOverlayVisible);
        Assert.True(state.ShouldShowBlockingLoadingMask);
        Assert.True(state.ShouldShowLoadingOverlayPresenter);
        Assert.Contains("Switching chat", state.OverlayStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenShellHasNewLatestIntent_HidesConversationChromeBeforeCommit()
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-1",
            MessageHistoryCount: 1,
            VisibleTranscriptConversationId: "conv-1",
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: false,
            IsInitializing: false,
            IsHydrating: false,
            IsLayoutLoading: false,
            IsSessionSwitching: false,
            SessionSwitchOverlayConversationId: null,
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: null,
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: "conv-2",
            HydrationLoadedMessageCount: 0));

        Assert.False(state.ShouldShowActiveConversationRoot);
        Assert.False(state.ShouldShowSessionHeader);
        Assert.False(state.ShouldShowTranscriptSurface);
        Assert.False(state.ShouldShowConversationInputSurface);
    }

    [Fact]
    public void Resolve_WhenShellHasNewLatestIntent_UsesShellIntentAsActivationOverlayOwner()
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-1",
            MessageHistoryCount: 1,
            VisibleTranscriptConversationId: "conv-1",
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: false,
            IsInitializing: false,
            IsHydrating: false,
            IsLayoutLoading: false,
            IsSessionSwitching: false,
            SessionSwitchOverlayConversationId: null,
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: null,
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: "conv-2",
            HydrationLoadedMessageCount: 0));

        Assert.True(state.IsActivationOverlayVisible);
        Assert.True(state.ShouldShowBlockingLoadingMask);
        Assert.True(state.ShouldShowLoadingOverlayPresenter);
        Assert.True(state.ShouldShowLoadingOverlayStatusPill);
        Assert.Equal(ChatViewModel.LoadingOverlayStage.PreparingSession, state.OverlayLoadingStage);
    }

    [Fact]
    public void Resolve_WhenShellIntentStartsBeforeSessionIsActive_UsesShellIntentAsActivationOverlayOwner()
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(new ChatConversationSurfaceStateInput(
            IsSessionActive: false,
            CurrentSessionId: null,
            MessageHistoryCount: 0,
            VisibleTranscriptConversationId: null,
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: false,
            IsInitializing: false,
            IsHydrating: false,
            IsLayoutLoading: false,
            IsSessionSwitching: false,
            SessionSwitchOverlayConversationId: null,
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: null,
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: "conv-2",
            HydrationLoadedMessageCount: 0));

        Assert.True(state.IsActivationOverlayVisible);
        Assert.True(state.ShouldShowBlockingLoadingMask);
        Assert.True(state.ShouldShowLoadingOverlayPresenter);
        Assert.True(state.ShouldShowLoadingOverlayStatusPill);
        Assert.Equal(ChatViewModel.LoadingOverlayStage.PreparingSession, state.OverlayLoadingStage);
    }
}
