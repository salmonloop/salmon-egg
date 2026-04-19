namespace SalmonEgg.Presentation.Core.Services;

public enum SessionActivationPhase
{
    None = 0,
    NavigatingToChatShell = 1,
    SelectingConversation = 2,
    Selected = 3,
    RemoteConnectionReady = 4,
    RemoteHydrationPending = 5,
    Hydrated = 6,
    Faulted = 7
}
