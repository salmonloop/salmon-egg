namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Input;

public sealed class ChatInputStatePresenter
{
    public ChatComposerPresentationState Present(ChatInputStateInput input)
    {
        if (input.IsVoiceInputListening)
        {
            var canStopVoiceInput = input.VoiceInputTransportState != VoiceInputTransportState.Stopping;
            return new ChatComposerPresentationState(
                Mode: ChatComposerMode.VoiceListening,
                IsTextInputEnabled: true,
                AreComposerToolsEnabled: false,
                CanSendPrompt: false,
                ShowCancelButton: false,
                CanCancelPrompt: false,
                CanStartVoiceInput: false,
                ShowVoiceStartButton: false,
                ShowVoiceStopButton: canStopVoiceInput,
                CanStopVoiceInput: canStopVoiceInput,
                ShowVoiceListeningStatus: canStopVoiceInput);
        }

        if (input.IsPromptSubmitInFlight)
        {
            return new ChatComposerPresentationState(
                Mode: ChatComposerMode.PromptInFlight,
                IsTextInputEnabled: false,
                AreComposerToolsEnabled: false,
                CanSendPrompt: false,
                ShowCancelButton: true,
                CanCancelPrompt: true,
                CanStartVoiceInput: false,
                ShowVoiceStartButton: input.IsVoiceInputSupported,
                ShowVoiceStopButton: false,
                CanStopVoiceInput: false,
                ShowVoiceListeningStatus: false);
        }

        var isSurfaceBlocked =
            input.IsBusy
            || input.HasPendingAskUserRequest
            || input.ShouldShowLoadingOverlayPresenter;

        var canSendPrompt =
            !isSurfaceBlocked
            && !input.IsPromptInFlight
            && input.IsSessionActive
            && input.HasChatService
            && input.IsInitialized
            && input.HasCurrentSessionId
            && input.HasPromptText;

        var canStartVoiceInput =
            input.IsVoiceInputSupported
            && !isSurfaceBlocked
            && !input.IsVoiceInputTransportBusy;

        return new ChatComposerPresentationState(
            Mode: ChatComposerMode.Enabled,
            IsTextInputEnabled: !isSurfaceBlocked,
            AreComposerToolsEnabled: !isSurfaceBlocked,
            CanSendPrompt: canSendPrompt,
            ShowCancelButton: input.IsPromptInFlight,
            CanCancelPrompt: input.IsPromptInFlight,
            CanStartVoiceInput: canStartVoiceInput,
            ShowVoiceStartButton: input.IsVoiceInputSupported,
            ShowVoiceStopButton: false,
            CanStopVoiceInput: false,
            ShowVoiceListeningStatus: false);
    }
}
