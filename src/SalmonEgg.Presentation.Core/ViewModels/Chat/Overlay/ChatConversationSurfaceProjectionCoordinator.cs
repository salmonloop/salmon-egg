using System;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;

internal sealed class ChatConversationSurfaceProjectionCoordinator
{
    private bool _hasLoadedActiveConversationRoot;
    private bool _hasLoadedTranscriptSurface;
    private int _highestOverlayStageRank;
    private ChatViewModel.LoadingOverlayStage _highestOverlayLoadingStage;
    private string _highestOverlayStatusText = string.Empty;

    public ChatConversationSurfaceProjection Project(ChatConversationSurfaceStateInput input)
    {
        var state = ChatConversationSurfaceStatePresenter.Resolve(input);

        _hasLoadedActiveConversationRoot |= state.ShouldShowActiveConversationRoot;
        _hasLoadedTranscriptSurface |= state.ShouldShowTranscriptSurface;

        var effectiveOverlayLoadingStage = state.OverlayLoadingStage;
        var effectiveOverlayStatusText = state.OverlayStatusText;
        var effectiveShouldShowLoadingOverlayStatusPill = state.ShouldShowLoadingOverlayStatusPill;
        var effectiveShouldShowLoadingOverlayPresenter = state.ShouldShowLoadingOverlayPresenter;

        if (!state.IsActivationOverlayVisible)
        {
            _highestOverlayStageRank = 0;
            _highestOverlayLoadingStage = ChatViewModel.LoadingOverlayStage.None;
            _highestOverlayStatusText = string.Empty;
        }
        else
        {
            var newRank = GetStageRank(state.OverlayLoadingStage);
            if (newRank >= _highestOverlayStageRank)
            {
                _highestOverlayStageRank = newRank;
                _highestOverlayLoadingStage = state.OverlayLoadingStage;
                _highestOverlayStatusText = state.OverlayStatusText ?? string.Empty;
            }

            effectiveOverlayLoadingStage = _highestOverlayLoadingStage;
            effectiveOverlayStatusText = _highestOverlayStatusText;

            // Recompute dependent visibility flags based on the potentially restored text
            effectiveShouldShowLoadingOverlayStatusPill = state.IsActivationOverlayVisible && !string.IsNullOrWhiteSpace(effectiveOverlayStatusText);

            if (effectiveShouldShowLoadingOverlayStatusPill)
            {
                effectiveShouldShowLoadingOverlayPresenter = true;
            }
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
            effectiveShouldShowLoadingOverlayStatusPill,
            effectiveShouldShowLoadingOverlayPresenter,
            effectiveOverlayLoadingStage,
            effectiveOverlayStatusText);
    }

    private static int GetStageRank(ChatViewModel.LoadingOverlayStage stage) => stage switch
    {
        ChatViewModel.LoadingOverlayStage.None => 0,
        ChatViewModel.LoadingOverlayStage.PreparingSession => 1,
        ChatViewModel.LoadingOverlayStage.Connecting => 2,
        ChatViewModel.LoadingOverlayStage.InitializingProtocol => 3,
        ChatViewModel.LoadingOverlayStage.HydratingHistory => 4,
        _ => 0
    };
}
