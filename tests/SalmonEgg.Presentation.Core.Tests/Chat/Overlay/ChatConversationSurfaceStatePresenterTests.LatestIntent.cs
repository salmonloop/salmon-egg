using SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Overlay;

public sealed class ChatConversationSurfaceStatePresenterTests_LatestIntent
{
    [Fact]
    public void Resolve_WhenShellHasNewLatestIntentButCurrentSessionIsConnecting_ShowsPreparingSession()
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-1",
            MessageHistoryCount: 0,
            VisibleTranscriptConversationId: null,
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: true,
            IsInitializing: false,
            IsHydrating: false,
            IsLayoutLoading: false,
            IsSessionSwitching: false,
            SessionSwitchOverlayConversationId: null,
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: "conv-1",
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: "conv-2",
            HydrationLoadedMessageCount: 0));

        Assert.Equal(ChatViewModel.LoadingOverlayStage.PreparingSession, state.OverlayLoadingStage);
    }
}
