namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed record StartComposerSnapshot(
    StartComposerStage Stage,
    bool IsExpanded,
    bool ShowHeroSuggestions,
    bool ShowPreflightSuggestions,
    bool ShowHeroChrome,
    bool FreezeComposerInteractions);
