namespace SalmonEgg.Presentation.ViewModels.Start;

public abstract record StartComposerAction;

public sealed record Loaded : StartComposerAction;

public sealed record Unloaded : StartComposerAction;

public sealed record Activated : StartComposerAction;

public sealed record FocusEntered : StartComposerAction;

public sealed record FocusExited : StartComposerAction;

public sealed record PopupOpened : StartComposerAction;

public sealed record PopupClosed : StartComposerAction;

public sealed record DraftChanged(bool HasDraft) : StartComposerAction;

public sealed record SuggestionApplied : StartComposerAction;

public sealed record OutsidePointerPressed : StartComposerAction;

public sealed record SubmitStarted : StartComposerAction;

public sealed record SubmitCompleted : StartComposerAction;
