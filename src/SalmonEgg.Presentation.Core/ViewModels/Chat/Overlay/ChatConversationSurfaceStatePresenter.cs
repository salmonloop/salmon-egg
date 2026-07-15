using System;
using System.Globalization;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;

internal static class ChatConversationSurfaceStatePresenter
{
    public static ChatConversationSurfaceState Resolve(
        ChatConversationSurfaceStateInput input,
        IStringLocalizer<CoreStrings>? localizer = null)
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
        var isShellActivationIntentVisible =
            !string.IsNullOrWhiteSpace(input.PendingShellActivationConversationId)
            && (!input.IsSessionActive
                || !MatchesCurrentSession(input.CurrentSessionId, input.PendingShellActivationConversationId)
                || !input.IsChatShellVisibleForRemoteUi);
        var shouldBlockCurrentConversationContentForActivation =
            shouldShowHistoryOverlay
            || shouldShowProjectedHydrationOverlay
            || isSessionSwitchOverlayVisible;

        var activationOverlayVisible =
            shouldShowConnectionLifecycleOverlay
            || shouldShowHistoryOverlay
            || shouldShowProjectedHydrationOverlay
            || isSessionSwitchOverlayVisible
            || isSessionSwitchPreviewVisible
            || isShellActivationIntentVisible;

        var overlayLoadingStage = ResolveOverlayLoadingStage(
            input.IsConnecting,
            input.IsInitializing,
            shouldShowConnectionLifecycleOverlay,
            shouldShowHistoryOverlay,
            shouldShowProjectedHydrationOverlay,
            isSessionSwitchOverlayVisible,
            isSessionSwitchPreviewVisible,
            isShellActivationIntentVisible,
            isSessionSwitchOverlayBlockingVisibleTranscript);
        var overlayStatusText = ResolveOverlayStatusText(overlayLoadingStage, input.HydrationLoadedMessageCount, localizer);
        var shouldShowBlockingLoadingMask =
            (activationOverlayVisible
                && (!hasVisibleTranscriptContent
                    || shouldBlockCurrentConversationContentForActivation
                    || isSessionSwitchOverlayBlockingVisibleTranscript
                    || isVisibleTranscriptStaleForCurrentSession
                    || isCurrentVisibleConversationSupersededByShellIntent));
        var shouldShowLoadingOverlayStatusPill =
            activationOverlayVisible && !string.IsNullOrWhiteSpace(overlayStatusText);
        var shouldShowLoadingOverlayPresenter =
            activationOverlayVisible && (shouldShowBlockingLoadingMask || shouldShowLoadingOverlayStatusPill);
        var isOverlayVisible = activationOverlayVisible;
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
        bool sessionSwitchPreviewVisible,
        bool shellActivationIntentVisible,
        bool isSessionSwitchOverlayBlockingVisibleTranscript)
    {
        if (shellActivationIntentVisible || isSessionSwitchOverlayBlockingVisibleTranscript)
        {
            return ChatViewModel.LoadingOverlayStage.PreparingSession;
        }

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

    private static string ResolveOverlayStatusText(
        ChatViewModel.LoadingOverlayStage stage,
        long hydrationLoadedMessageCount,
        IStringLocalizer<CoreStrings>? localizer)
        => stage switch
        {
            ChatViewModel.LoadingOverlayStage.Connecting => Localize(
                localizer,
                "ChatLoading_Connecting",
                "Connecting to assistant..."),
            ChatViewModel.LoadingOverlayStage.InitializingProtocol => Localize(
                localizer,
                "ChatLoading_InitializingProtocol",
                "Preparing chat environment..."),
            ChatViewModel.LoadingOverlayStage.HydratingHistory => BuildHydrationStatusText(
                hydrationLoadedMessageCount,
                localizer),
            ChatViewModel.LoadingOverlayStage.PreparingSession => Localize(
                localizer,
                "ChatLoading_PreparingSession",
                "Switching chat..."),
            _ => string.Empty
        };

    private static string BuildHydrationStatusText(long loadedCount, IStringLocalizer<CoreStrings>? localizer)
        => loadedCount > 0
            ? FormatLocalize(
                localizer,
                "ChatLoading_HydratingHistoryWithCount",
                "Loading chat history ({0} messages loaded)",
                loadedCount)
            : Localize(
                localizer,
                "ChatLoading_HydratingHistory",
                "Loading chat history...");

    private static string Localize(IStringLocalizer<CoreStrings>? localizer, string key, string fallback)
    {
        if (localizer is null)
        {
            return fallback;
        }

        var localized = localizer[key];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }

    private static string FormatLocalize(
        IStringLocalizer<CoreStrings>? localizer,
        string key,
        string fallback,
        params object[] arguments)
    {
        if (localizer is null)
        {
            return string.Format(CultureInfo.CurrentCulture, fallback, arguments);
        }

        var localized = localizer[key, arguments];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? string.Format(CultureInfo.CurrentCulture, fallback, arguments)
            : localized.Value;
    }
}
