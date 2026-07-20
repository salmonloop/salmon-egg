using SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Overlay;

public sealed class ChatConversationSurfaceProjectionCoordinatorStageRegressionTests
{
    [Fact]
    public void Project_OverlayStage_DoesNotRegressToPreparingSessionAfterConnecting()
    {
        var coordinator = new ChatConversationSurfaceProjectionCoordinator();

        // 1. First, we are preparing (session switch is visible)
        var prep = coordinator.Project(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-1",
            MessageHistoryCount: 0,
            VisibleTranscriptConversationId: null,
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: false,
            IsInitializing: false,
            IsHydrating: false,
            IsLayoutLoading: false,
            IsSessionSwitching: true,
            SessionSwitchOverlayConversationId: "conv-2",
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: null,
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 0));

        Assert.Equal(ChatViewModel.LoadingOverlayStage.PreparingSession, prep.OverlayLoadingStage);

        // 2. Then we start connecting
        var conn = coordinator.Project(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-2", // switched
            MessageHistoryCount: 0,
            VisibleTranscriptConversationId: null,
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: true,
            IsInitializing: false,
            IsHydrating: false,
            IsLayoutLoading: false,
            IsSessionSwitching: true,
            SessionSwitchOverlayConversationId: "conv-2",
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: "conv-2",
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 0));

        Assert.Equal(ChatViewModel.LoadingOverlayStage.Connecting, conn.OverlayLoadingStage);

        // 3. Then connecting finishes, but session switch overlay is still active for a moment
        var regress = coordinator.Project(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-2",
            MessageHistoryCount: 0,
            VisibleTranscriptConversationId: null,
            IsChatShellVisibleForRemoteUi: true,
            IsConnecting: false,
            IsInitializing: false,
            IsHydrating: false,
            IsLayoutLoading: false,
            IsSessionSwitching: true,
            SessionSwitchOverlayConversationId: "conv-2",
            SessionSwitchPreviewConversationId: null,
            ConnectionLifecycleOverlayConversationId: "conv-2",
            HistoryOverlayConversationId: null,
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 0));

        // It should NOT regress to PreparingSession. It should be at least Connecting (or maybe None if it's done).
        // Wait, if we enforce monotonicity, it should stay at Connecting (or advance to something else).
        Assert.NotEqual(ChatViewModel.LoadingOverlayStage.PreparingSession, regress.OverlayLoadingStage);
    }
}
