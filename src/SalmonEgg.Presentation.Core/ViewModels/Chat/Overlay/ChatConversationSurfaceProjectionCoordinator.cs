using System;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;

internal sealed class ChatConversationSurfaceProjectionCoordinator
{
    private readonly IStringLocalizer<CoreStrings>? _localizer;
    private bool _hasLoadedActiveConversationRoot;
    private bool _hasLoadedTranscriptSurface;

    public ChatConversationSurfaceProjectionCoordinator(IStringLocalizer<CoreStrings>? localizer = null)
    {
        _localizer = localizer;
    }

    public ChatConversationSurfaceProjection Project(ChatConversationSurfaceStateInput input)
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(input, _localizer);

        _hasLoadedActiveConversationRoot |= state.ShouldShowActiveConversationRoot;
        _hasLoadedTranscriptSurface |= state.ShouldShowTranscriptSurface;

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
