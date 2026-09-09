namespace SalmonEgg.Domain.Services;

/// <summary>
/// Represents one reusable local interactive terminal session bound to a conversation.
/// </summary>
public interface ILocalTerminalSession : IInteractiveTerminalSession
{
    string ConversationId { get; }

    string CurrentWorkingDirectory { get; }
}
