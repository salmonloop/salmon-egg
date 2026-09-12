namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public enum ChatTurnPhase
{
    CreatingRemoteSession,
    DispatchingPrompt,
    WaitingForAgent,
    Thinking,
    ToolPending,
    ToolRunning,
    Responding,
    Completed,
    Failed,
    Cancelled,
    WaitingForUser
}
