namespace SalmonEgg.Presentation.ViewModels.Chat.Hydration;

internal enum ConversationHydrationPhase
{
    AwaitingReplayStart = 1,
    ReplayingSessionUpdates = 2,
    ProjectingTranscript = 3,
    SettlingReplay = 4,
    FinalizingProjection = 5
}
