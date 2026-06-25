using System;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;

internal sealed class ChatConversationSurfaceProjectionCoordinator
{
    public ChatConversationSurfaceProjection Project(ChatConversationSurfaceStateInput input)
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(input);

        return new ChatConversationSurfaceProjection(
            state.IsActivationOverlayVisible,
            state.IsOverlayVisible,
            state.ShouldShowActiveConversationRoot,
            state.ShouldShowActiveConversationRoot,
            state.ShouldShowSessionHeader,
            state.ShouldShowTranscriptSurface,
            state.ShouldShowTranscriptSurface,
            state.ShouldShowConversationInputSurface,
            state.ShouldShowBlockingLoadingMask,
            state.ShouldShowLoadingOverlayStatusPill,
            state.ShouldShowLoadingOverlayPresenter,
            state.OverlayLoadingStage,
            state.OverlayStatusText);
    }
}
