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
    bool ShowVoiceListeningStatus)
{
    public bool IsInputEnabled => IsTextInputEnabled;

    public bool IsInteractiveSurfaceEnabled => IsTextInputEnabled && AreComposerToolsEnabled;
}
