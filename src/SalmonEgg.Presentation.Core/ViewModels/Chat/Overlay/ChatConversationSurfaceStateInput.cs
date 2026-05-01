namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;

internal readonly record struct ChatConversationSurfaceStateInput(
    bool IsSessionActive,
    string? CurrentSessionId,
    int MessageHistoryCount,
    string? VisibleTranscriptConversationId,
    bool IsChatShellVisibleForRemoteUi,
    bool IsConnecting,
    bool IsInitializing,
    bool IsHydrating,
    bool IsLayoutLoading,
    bool IsSessionSwitching,
    string? SessionSwitchOverlayConversationId,
    string? SessionSwitchPreviewConversationId,
    string? ConnectionLifecycleOverlayConversationId,
    string? HistoryOverlayConversationId,
    string? PendingShellActivationConversationId,
    long HydrationLoadedMessageCount);
