namespace SalmonEgg.Presentation.Core.Services.Input;

public static class ChatInputNavigationPolicy
{
    public static ChatInputNavigationAction Decide(
        GamepadNavigationIntent intent,
        ChatInputFocusContext focusContext,
        bool slashCommandsVisible,
        bool inputEnabled,
        bool isImeComposing)
    {
        if (!slashCommandsVisible || !inputEnabled || isImeComposing)
        {
            return ChatInputNavigationAction.None;
        }

        if (focusContext != ChatInputFocusContext.InputBox)
        {
            return ChatInputNavigationAction.None;
        }

        return intent switch
        {
            GamepadNavigationIntent.MoveUp => ChatInputNavigationAction.MoveSlashUp,
            GamepadNavigationIntent.MoveDown => ChatInputNavigationAction.MoveSlashDown,
            GamepadNavigationIntent.Activate => ChatInputNavigationAction.AcceptSlashCommand,
            _ => ChatInputNavigationAction.None
        };
    }
}

public enum ChatInputFocusContext
{
    Other,
    InputBox,
    ModeSelector,
    SendButton,
    CancelButton
}

public enum ChatInputNavigationAction
{
    None,
    MoveSlashUp,
    MoveSlashDown,
    AcceptSlashCommand
}
