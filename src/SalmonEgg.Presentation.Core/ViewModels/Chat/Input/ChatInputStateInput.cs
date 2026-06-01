namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Input;

public sealed record ChatInputStateInput(
    bool IsBusy,
    bool IsPromptInFlight,
    bool IsPromptSubmitInFlight,
    bool IsVoiceInputListening,
    VoiceInputTransportState VoiceInputTransportState,
    bool HasPendingAskUserRequest,
    bool ShouldShowLoadingOverlayPresenter,
    bool IsSessionActive,
    bool HasChatService,
    bool IsInitialized,
    bool HasCurrentSessionId,
    bool HasPromptText,
    bool IsVoiceInputSupported)
{
    public bool IsVoiceInputTransportBusy => VoiceInputTransportState != VoiceInputTransportState.Idle;
}
