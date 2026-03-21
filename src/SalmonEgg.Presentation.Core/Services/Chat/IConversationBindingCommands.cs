namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IConversationBindingCommands
{
    ValueTask<BindingUpdateResult> UpdateBindingAsync(string conversationId, string? remoteSessionId, string? boundProfileId);
}
