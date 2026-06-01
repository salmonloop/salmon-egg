namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Input;

public sealed record VoiceInputUiState(
    VoiceInputUiMode Mode,
    bool CanStart,
    bool CanStop)
{
    public bool ShowStartButton => Mode == VoiceInputUiMode.Ready;

    public bool ShowStopButton => Mode == VoiceInputUiMode.Listening;

    public bool ShowProgressRing => Mode == VoiceInputUiMode.Transitioning;

    public bool ShowListeningStatus => Mode == VoiceInputUiMode.Listening;
}
