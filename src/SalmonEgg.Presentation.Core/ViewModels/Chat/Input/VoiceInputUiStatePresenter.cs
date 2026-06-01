namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Input;

public sealed class VoiceInputUiStatePresenter
{
    public VoiceInputUiState Present(VoiceInputUiStateInput input)
    {
        if (!input.IsVoiceInputSupported && !input.IsVoiceInputListening)
        {
            return new VoiceInputUiState(VoiceInputUiMode.Hidden, CanStart: false, CanStop: false);
        }

        if (input.TransportState == VoiceInputTransportState.Authorizing
            || input.TransportState == VoiceInputTransportState.Stopping
            || (input.TransportState == VoiceInputTransportState.Starting && !input.IsVoiceInputListening))
        {
            return new VoiceInputUiState(
                VoiceInputUiMode.Transitioning,
                CanStart: false,
                CanStop: false);
        }

        if (input.IsVoiceInputListening)
        {
            return new VoiceInputUiState(
                VoiceInputUiMode.Listening,
                CanStart: false,
                CanStop: input.CanStopVoiceInput);
        }

        return new VoiceInputUiState(
            VoiceInputUiMode.Ready,
            CanStart: input.CanStartVoiceInput,
            CanStop: false);
    }
}
