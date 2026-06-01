namespace SalmonEgg.Presentation.Core.Services.Input;

public static class ChatVoiceShortcutPolicy
{
    public static ChatVoiceShortcutAction Decide(
        GamepadShortcutIntent intent,
        ChatVoiceShortcutFocusContext focusContext,
        bool canStartVoiceInput,
        bool canStopVoiceInput,
        bool isVoiceInputListening,
        bool isImeComposing)
    {
        if (intent != GamepadShortcutIntent.ToggleVoiceInput
            || focusContext != ChatVoiceShortcutFocusContext.InputBox
            || isImeComposing)
        {
            return ChatVoiceShortcutAction.None;
        }

        if (isVoiceInputListening && canStopVoiceInput)
        {
            return ChatVoiceShortcutAction.StopVoiceInput;
        }

        return canStartVoiceInput
            ? ChatVoiceShortcutAction.StartVoiceInput
            : ChatVoiceShortcutAction.None;
    }
}
