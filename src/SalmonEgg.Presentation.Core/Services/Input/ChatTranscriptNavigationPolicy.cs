namespace SalmonEgg.Presentation.Core.Services.Input;

public enum ChatTranscriptNavigationAction
{
    None = 0,
    ScrollByItems = 1,
}

public readonly record struct ChatTranscriptNavigationDecision(
    ChatTranscriptNavigationAction Action,
    int ItemDelta)
{
    public static ChatTranscriptNavigationDecision None { get; } = new(
        ChatTranscriptNavigationAction.None,
        0);
}

public static class ChatTranscriptNavigationPolicy
{
    public static ChatTranscriptNavigationDecision Resolve(
        GamepadNavigationIntent intent,
        bool hasTranscriptFocus,
        int messageCount)
    {
        if (!hasTranscriptFocus || messageCount <= 0)
        {
            return ChatTranscriptNavigationDecision.None;
        }

        return intent switch
        {
            GamepadNavigationIntent.MoveUp => new(ChatTranscriptNavigationAction.ScrollByItems, -1),
            GamepadNavigationIntent.MoveDown => new(ChatTranscriptNavigationAction.ScrollByItems, 1),
            _ => ChatTranscriptNavigationDecision.None,
        };
    }
}

public static class ChatTranscriptNavigationIntentHandler
{
    public static bool TryConsume(
        GamepadNavigationIntent intent,
        bool hasTranscriptFocus,
        int messageCount,
        Func<int, bool> tryScrollByItems,
        Action registerUserViewportIntent)
    {
        ArgumentNullException.ThrowIfNull(tryScrollByItems);
        ArgumentNullException.ThrowIfNull(registerUserViewportIntent);

        var decision = ChatTranscriptNavigationPolicy.Resolve(
            intent,
            hasTranscriptFocus,
            messageCount);
        if (decision.Action != ChatTranscriptNavigationAction.ScrollByItems)
        {
            return false;
        }

        if (tryScrollByItems(decision.ItemDelta))
        {
            registerUserViewportIntent();
        }

        return true;
    }
}
