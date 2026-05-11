namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Input;

public sealed class ChatInputStatePresenter
{
    public ChatComposerPresentationState Present(ChatInputStateInput input)
    {
        if (input.IsVoiceInputListening)
        {
            return new ChatComposerPresentationState(
                Mode: ChatComposerMode.VoiceListening,
                IsTextInputEnabled: true,
                AreComposerToolsEnabled: false,
                CanSendPrompt: false,
                ShowCancelButton: false,
                CanCancelPrompt: false,
                CanStartVoiceInput: false,
                ShowVoiceStartButton: false,
                ShowVoiceStopButton: true,
                CanStopVoiceInput: true,
                ShowPromptInFlightStatus: false,
                ShowVoiceListeningStatus: true);
        }

        if (input.IsPromptInFlight)
        {
            return new ChatComposerPresentationState(
                Mode: ChatComposerMode.PromptInFlight,
                IsTextInputEnabled: false,
                AreComposerToolsEnabled: false,
                CanSendPrompt: false,
                ShowCancelButton: true,
                CanCancelPrompt: true,
                CanStartVoiceInput: false,
                ShowVoiceStartButton: false,
                ShowVoiceStopButton: false,
                CanStopVoiceInput: false,
                ShowPromptInFlightStatus: true,
                ShowVoiceListeningStatus: false);
        }

        var isSurfaceBlocked =
            input.IsBusy
            || input.IsVoiceInputBusy;

        var canSendPrompt =
            !isSurfaceBlocked
            && input.IsSessionActive
            && input.HasChatService
            && input.IsInitialized
            && input.HasCurrentSessionId
            && input.HasPromptText;

        var canStartVoiceInput =
            input.IsVoiceInputSupported
            && !isSurfaceBlocked;

        return new ChatComposerPresentationState(
            Mode: ChatComposerMode.Enabled,
            IsTextInputEnabled: !isSurfaceBlocked,
            AreComposerToolsEnabled: !isSurfaceBlocked,
            CanSendPrompt: canSendPrompt,
            ShowCancelButton: false,
            CanCancelPrompt: false,
            CanStartVoiceInput: canStartVoiceInput,
            ShowVoiceStartButton: input.IsVoiceInputSupported,
            ShowVoiceStopButton: false,
            CanStopVoiceInput: false,
            ShowPromptInFlightStatus: false,
            ShowVoiceListeningStatus: false);
    }
}
