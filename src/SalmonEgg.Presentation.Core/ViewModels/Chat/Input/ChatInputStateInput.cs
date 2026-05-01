namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Input;

public sealed record ChatInputStateInput(
    bool IsBusy,
    bool IsPromptInFlight,
    bool IsVoiceInputListening,
    bool IsVoiceInputBusy,
    bool HasPendingAskUserRequest,
    bool ShouldShowLoadingOverlayPresenter,
    bool IsSessionActive,
    bool HasChatService,
    bool IsInitialized,
    bool HasCurrentSessionId,
    bool HasPromptText,
    bool IsVoiceInputSupported);
