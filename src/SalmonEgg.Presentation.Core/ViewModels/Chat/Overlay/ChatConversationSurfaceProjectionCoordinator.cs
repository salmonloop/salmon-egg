using System;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;

internal sealed class ChatConversationSurfaceProjectionCoordinator
{
    private bool _hasLoadedActiveConversationRoot;
    private bool _hasLoadedTranscriptSurface;
    private bool _hasLoadedConversationInputSurface;

    public ChatConversationSurfaceProjection Project(ChatConversationSurfaceStateInput input)
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(input);

        _hasLoadedActiveConversationRoot |= state.ShouldShowActiveConversationRoot;
        _hasLoadedTranscriptSurface |= state.ShouldShowTranscriptSurface;
        _hasLoadedConversationInputSurface |= state.ShouldShowConversationInputSurface;

        return new ChatConversationSurfaceProjection(
            state.IsActivationOverlayVisible,
            state.IsOverlayVisible,
            state.ShouldShowActiveConversationRoot,
            _hasLoadedActiveConversationRoot,
            state.ShouldShowSessionHeader,
            state.ShouldShowTranscriptSurface,
            _hasLoadedTranscriptSurface,
            state.ShouldShowConversationInputSurface,
            _hasLoadedConversationInputSurface,
            state.ShouldShowBlockingLoadingMask,
            state.ShouldShowLoadingOverlayStatusPill,
            state.ShouldShowLoadingOverlayPresenter,
            state.OverlayLoadingStage,
            state.OverlayStatusText);
    }
}
