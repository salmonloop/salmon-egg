namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IConversationBindingCommands
{
    void UpdateRemoteBinding(string conversationId, string? remoteSessionId, string? boundProfileId);

    void ScheduleSave();
}
