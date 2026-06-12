using System;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;

internal sealed class ChatConversationSurfaceProjectionCoordinator
{
    private bool _hasLoadedActiveConversationRoot;
    private bool _hasLoadedTranscriptSurface;

    public ChatConversationSurfaceProjection Project(ChatConversationSurfaceStateInput input)
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(input);

        if (state.ShouldShowActiveConversationRoot)
        {
            _hasLoadedActiveConversationRoot = true;
        }

        if (state.ShouldShowTranscriptSurface)
        {
            _hasLoadedTranscriptSurface = true;
        }

        return new ChatConversationSurfaceProjection(
            state.IsActivationOverlayVisible,
            state.IsOverlayVisible,
            state.ShouldShowActiveConversationRoot,
            _hasLoadedActiveConversationRoot,
            state.ShouldShowSessionHeader,
            state.ShouldShowTranscriptSurface,
            _hasLoadedTranscriptSurface,
            state.ShouldShowConversationInputSurface,
            state.ShouldShowBlockingLoadingMask,
            state.ShouldShowLoadingOverlayStatusPill,
            state.ShouldShowLoadingOverlayPresenter,
            state.OverlayLoadingStage,
            state.OverlayStatusText);
    }
}
