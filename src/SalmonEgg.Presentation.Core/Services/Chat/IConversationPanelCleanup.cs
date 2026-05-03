namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Abstraction for post-mutation UI state cleanup that lives in the presentation layer.
/// Implemented by ChatViewModel to decouple ConversationCatalogFacade from ViewModel internals.
/// </summary>
public interface IConversationPanelCleanup
{
    void CleanupAfterMutation(string conversationId, bool isCurrentSession);
}
