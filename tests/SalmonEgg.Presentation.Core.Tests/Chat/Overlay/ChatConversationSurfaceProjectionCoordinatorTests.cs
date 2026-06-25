using SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Overlay;

public sealed class ChatConversationSurfaceProjectionCoordinatorTests
{
    [Fact]
    public void Project_WhenActiveConversationRootWasShown_KeepsLoadStateWhenLaterHidden()
    {
        var coordinator = new ChatConversationSurfaceProjectionCoordinator();

        var visible = coordinator.Project(new ChatConversationSurfaceStateInput(
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
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 1));

        var hidden = coordinator.Project(new ChatConversationSurfaceStateInput(
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
            HydrationLoadedMessageCount: 1));

        Assert.True(visible.ShouldShowActiveConversationRoot);
        Assert.True(visible.ShouldLoadActiveConversationRoot);
        Assert.False(hidden.ShouldShowActiveConversationRoot);
        Assert.False(hidden.ShouldLoadActiveConversationRoot);
    }

    [Fact]
    public void Project_WhenTranscriptSurfaceWasShown_KeepsLoadStateWhenLaterHidden()
    {
        var coordinator = new ChatConversationSurfaceProjectionCoordinator();

        var visible = coordinator.Project(new ChatConversationSurfaceStateInput(
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
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 1));

        var hidden = coordinator.Project(new ChatConversationSurfaceStateInput(
            IsSessionActive: true,
            CurrentSessionId: "conv-1",
            MessageHistoryCount: 0,
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
            PendingShellActivationConversationId: null,
            HydrationLoadedMessageCount: 0));

        Assert.True(visible.ShouldShowTranscriptSurface);
        Assert.True(visible.ShouldLoadTranscriptSurface);
        Assert.False(hidden.ShouldShowTranscriptSurface);
        Assert.False(hidden.ShouldLoadTranscriptSurface);
    }
}
