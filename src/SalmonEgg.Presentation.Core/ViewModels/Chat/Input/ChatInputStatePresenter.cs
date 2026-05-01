namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Input;

public sealed class ChatInputStatePresenter
{
    public ChatInputState Present(ChatInputStateInput input)
    {
        var isInputEnabled =
            !input.IsBusy
            && !input.IsPromptInFlight
            && !input.IsVoiceInputListening
            && !input.IsVoiceInputBusy
            && !input.HasPendingAskUserRequest
            && !input.ShouldShowLoadingOverlayPresenter;

        var canSendPrompt =
            isInputEnabled
            && input.IsSessionActive
            && input.HasChatService
            && input.IsInitialized
            && input.HasCurrentSessionId
            && input.HasPromptText;

        var canStartVoiceInput =
            input.IsVoiceInputSupported
            && !input.IsVoiceInputListening
            && !input.IsVoiceInputBusy
            && isInputEnabled;

        var canStopVoiceInput =
            input.IsVoiceInputSupported
            && input.IsVoiceInputListening
            && !input.IsVoiceInputBusy;

        return new ChatInputState(
            IsInputEnabled: isInputEnabled,
            CanSendPrompt: canSendPrompt,
            CanStartVoiceInput: canStartVoiceInput,
            CanStopVoiceInput: canStopVoiceInput,
            ShowVoiceInputStartButton: input.IsVoiceInputSupported && !input.IsVoiceInputListening,
            ShowVoiceInputStopButton: input.IsVoiceInputSupported && input.IsVoiceInputListening);
    }
}
