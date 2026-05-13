namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed record StartSessionModeSnapshot(
    StartSessionModeStage Stage,
    bool IsEnabled);
