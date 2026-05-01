namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Input;

public sealed record ChatInputState(
    bool IsInputEnabled,
    bool CanSendPrompt,
    bool CanStartVoiceInput,
    bool CanStopVoiceInput,
    bool ShowVoiceInputStartButton,
    bool ShowVoiceInputStopButton);
