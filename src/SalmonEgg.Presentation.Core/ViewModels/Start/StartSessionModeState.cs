namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed record StartSessionModeState(
    bool IsStarting,
    bool IsConnectionReady,
    bool IsConnectionInProgress,
    bool IsDraftRefreshPending,
    bool IsDraftLoading,
    bool IsDraftReady,
    int ModeCount);
