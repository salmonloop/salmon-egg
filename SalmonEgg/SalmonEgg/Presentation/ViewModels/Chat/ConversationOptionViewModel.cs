namespace SalmonEgg.Presentation.ViewModels.Chat;

public sealed class ConversationOptionViewModel
{
    public ConversationOptionViewModel(string conversationId, string displayName)
    {
        ConversationId = conversationId;
        DisplayName = displayName;
    }

    public string ConversationId { get; }
    public string DisplayName { get; }
}
