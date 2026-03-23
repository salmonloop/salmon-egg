namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public enum ChatTurnPhase
{
    CreatingRemoteSession,
    WaitingForAgent,
    Thinking,
    ToolPending,
    ToolRunning,
    Responding,
    Completed,
    Failed,
    Cancelled
}
