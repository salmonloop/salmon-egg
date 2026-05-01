using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;

internal readonly record struct ChatConversationSurfaceState(
    bool IsActivationOverlayVisible,
    bool IsOverlayVisible,
    bool ShouldShowActiveConversationRoot,
    bool ShouldShowSessionHeader,
    bool ShouldShowTranscriptSurface,
    bool ShouldShowConversationInputSurface,
    bool ShouldShowBlockingLoadingMask,
    bool ShouldShowLoadingOverlayStatusPill,
    bool ShouldShowLoadingOverlayPresenter,
    ChatViewModel.LoadingOverlayStage OverlayLoadingStage,
    string OverlayStatusText);
