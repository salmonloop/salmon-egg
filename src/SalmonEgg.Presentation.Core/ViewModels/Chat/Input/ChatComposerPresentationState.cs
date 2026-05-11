namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Input;

public sealed record ChatComposerPresentationState(
    ChatComposerMode Mode,
    bool IsTextInputEnabled,
    bool AreComposerToolsEnabled,
    bool CanSendPrompt,
    bool ShowCancelButton,
    bool CanCancelPrompt,
    bool CanStartVoiceInput,
    bool ShowVoiceStartButton,
    bool ShowVoiceStopButton,
    bool CanStopVoiceInput,
    bool ShowPromptInFlightStatus,
    bool ShowVoiceListeningStatus)
{
    // Temporary bridge properties keep existing consumers compiling until the
    // broader ViewModel/XAML projection is updated to the unified composer contract.
    public bool IsInputEnabled => IsTextInputEnabled;

    public bool IsInteractiveSurfaceEnabled => IsTextInputEnabled && AreComposerToolsEnabled;

    public bool ShowVoiceInputStartButton => ShowVoiceStartButton;

    public bool ShowVoiceInputStopButton => ShowVoiceStopButton;
}
