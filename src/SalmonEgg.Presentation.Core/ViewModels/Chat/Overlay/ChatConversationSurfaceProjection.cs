using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;

internal readonly record struct ChatConversationSurfaceProjection(
    bool IsActivationOverlayVisible,
    bool IsOverlayVisible,
    bool ShouldShowActiveConversationRoot,
    bool ShouldLoadActiveConversationRoot,
    bool ShouldShowSessionHeader,
    bool ShouldShowTranscriptSurface,
    bool ShouldLoadTranscriptSurface,
    bool ShouldShowConversationInputSurface,
    bool ShouldShowBlockingLoadingMask,
    bool ShouldShowLoadingOverlayStatusPill,
    bool ShouldShowLoadingOverlayPresenter,
    ChatViewModel.LoadingOverlayStage OverlayLoadingStage,
    string OverlayStatusText);
