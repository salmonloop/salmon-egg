namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Input;

public sealed record VoiceInputUiStateInput(
    bool IsVoiceInputSupported,
    bool IsVoiceInputListening,
    VoiceInputTransportState TransportState,
    bool CanStartVoiceInput,
    bool CanStopVoiceInput);
