using System;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;

internal static class ChatConversationSurfaceStatePresenter
{
    public static ChatConversationSurfaceState Resolve(ChatConversationSurfaceStateInput input)
    {
        var hasVisibleTranscriptContent = input.MessageHistoryCount > 0;
        var isSessionSwitchOverlayVisible =
            input.IsSessionSwitching && !string.IsNullOrWhiteSpace(input.SessionSwitchOverlayConversationId);
        var isSessionSwitchPreviewVisible = !string.IsNullOrWhiteSpace(input.SessionSwitchPreviewConversationId);
        var shouldShowConnectionLifecycleOverlay =
            input.IsChatShellVisibleForRemoteUi
            && MatchesCurrentSession(input.CurrentSessionId, input.ConnectionLifecycleOverlayConversationId)
            && (input.IsConnecting || input.IsInitializing);
        var shouldShowHistoryOverlay =
            input.IsChatShellVisibleForRemoteUi
            && MatchesCurrentSession(input.CurrentSessionId, input.HistoryOverlayConversationId);
        var shouldShowProjectedHydrationOverlay =
            input.IsChatShellVisibleForRemoteUi
            && !shouldShowHistoryOverlay
            && input.IsHydrating
            && !string.IsNullOrWhiteSpace(input.CurrentSessionId);
        var shouldShowLayoutLoading = input.IsLayoutLoading && input.IsChatShellVisibleForRemoteUi;
        var isSessionSwitchOverlayBlockingVisibleTranscript =
            (isSessionSwitchPreviewVisible
                && !MatchesCurrentSession(input.CurrentSessionId, input.SessionSwitchPreviewConversationId))
            || (isSessionSwitchOverlayVisible
                && !MatchesCurrentSession(input.CurrentSessionId, input.SessionSwitchOverlayConversationId));
        var isVisibleTranscriptStaleForCurrentSession =
            hasVisibleTranscriptContent
            && !string.IsNullOrWhiteSpace(input.VisibleTranscriptConversationId)
            && !string.Equals(input.VisibleTranscriptConversationId, input.CurrentSessionId, StringComparison.Ordinal);
        var isCurrentVisibleConversationSupersededByShellIntent =
            input.IsSessionActive
            && !string.IsNullOrWhiteSpace(input.PendingShellActivationConversationId)
            && !string.Equals(input.PendingShellActivationConversationId, input.CurrentSessionId, StringComparison.Ordinal);
        var shouldPromoteLayoutLoadingToBlockingPresenter =
            input.IsLayoutLoading
            && (isVisibleTranscriptStaleForCurrentSession || isCurrentVisibleConversationSupersededByShellIntent);

        var activationOverlayVisible =
            shouldShowConnectionLifecycleOverlay
            || shouldShowHistoryOverlay
            || shouldShowProjectedHydrationOverlay
            || isSessionSwitchOverlayVisible
            || isSessionSwitchPreviewVisible;

        var overlayLoadingStage = ResolveOverlayLoadingStage(
            input.IsConnecting,
            input.IsInitializing,
            shouldShowConnectionLifecycleOverlay,
            shouldShowHistoryOverlay,
            shouldShowProjectedHydrationOverlay,
            isSessionSwitchOverlayVisible,
            isSessionSwitchPreviewVisible);
        var overlayStatusText = ResolveOverlayStatusText(overlayLoadingStage, input.HydrationLoadedMessageCount);
        var shouldShowBlockingLoadingMask =
            (activationOverlayVisible
                && (!hasVisibleTranscriptContent
                    || isSessionSwitchOverlayBlockingVisibleTranscript
                    || isVisibleTranscriptStaleForCurrentSession))
            || shouldPromoteLayoutLoadingToBlockingPresenter;
        var shouldShowLoadingOverlayStatusPill =
            activationOverlayVisible && !string.IsNullOrWhiteSpace(overlayStatusText);
        var shouldShowLoadingOverlayPresenter =
            (activationOverlayVisible && (shouldShowBlockingLoadingMask || shouldShowLoadingOverlayStatusPill))
            || shouldPromoteLayoutLoadingToBlockingPresenter;
        var isOverlayVisible = activationOverlayVisible || shouldShowLayoutLoading;
        var shouldShowActiveConversationRoot =
            input.IsSessionActive
            && !shouldShowBlockingLoadingMask
            && !isCurrentVisibleConversationSupersededByShellIntent;
        var shouldShowSessionHeader = shouldShowActiveConversationRoot;
        var shouldShowTranscriptSurface =
            shouldShowActiveConversationRoot
            && hasVisibleTranscriptContent
            && !isVisibleTranscriptStaleForCurrentSession;
        var shouldShowConversationInputSurface = shouldShowActiveConversationRoot;

        return new ChatConversationSurfaceState(
            activationOverlayVisible,
            isOverlayVisible,
            shouldShowActiveConversationRoot,
            shouldShowSessionHeader,
            shouldShowTranscriptSurface,
            shouldShowConversationInputSurface,
            shouldShowBlockingLoadingMask,
            shouldShowLoadingOverlayStatusPill,
            shouldShowLoadingOverlayPresenter,
            overlayLoadingStage,
            overlayStatusText);
    }

    private static bool MatchesCurrentSession(string? currentSessionId, string? ownerConversationId)
        => !string.IsNullOrWhiteSpace(ownerConversationId)
            && string.Equals(ownerConversationId, currentSessionId, StringComparison.Ordinal);

    private static ChatViewModel.LoadingOverlayStage ResolveOverlayLoadingStage(
        bool isConnecting,
        bool isInitializing,
        bool connectionLifecycleOverlayVisible,
        bool historyOverlayVisible,
        bool projectedHydrationOverlayVisible,
        bool sessionSwitchOverlayVisible,
        bool sessionSwitchPreviewVisible)
    {
        if (isConnecting && connectionLifecycleOverlayVisible)
        {
            return ChatViewModel.LoadingOverlayStage.Connecting;
        }

        if (isInitializing && connectionLifecycleOverlayVisible)
        {
            return ChatViewModel.LoadingOverlayStage.InitializingProtocol;
        }

        if (historyOverlayVisible || projectedHydrationOverlayVisible)
        {
            return ChatViewModel.LoadingOverlayStage.HydratingHistory;
        }

        if (sessionSwitchOverlayVisible || sessionSwitchPreviewVisible)
        {
            return ChatViewModel.LoadingOverlayStage.PreparingSession;
        }

        return ChatViewModel.LoadingOverlayStage.None;
    }

    private static string ResolveOverlayStatusText(ChatViewModel.LoadingOverlayStage stage, long hydrationLoadedMessageCount)
        => stage switch
        {
            ChatViewModel.LoadingOverlayStage.Connecting => "正在连接助手...",
            ChatViewModel.LoadingOverlayStage.InitializingProtocol => "正在准备聊天环境...",
            ChatViewModel.LoadingOverlayStage.HydratingHistory => BuildHydrationStatusText(hydrationLoadedMessageCount),
            ChatViewModel.LoadingOverlayStage.PreparingSession => "正在切换聊天...",
            _ => string.Empty
        };

    private static string BuildHydrationStatusText(long loadedCount)
        => FormatHydrationStatus("正在加载聊天记录", loadedCount);

    private static string FormatHydrationStatus(string baseText, long loadedCount)
        => loadedCount > 0
            ? $"{baseText}（已加载 {loadedCount} 条消息）"
            : $"{baseText}...";
}
