namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed record StartComposerState(
    bool HasFocusWithin = false,
    bool IsPopupOpen = false,
    bool HasDraft = false,
    bool IsSubmitting = false,
    bool WasExplicitlyActivated = false)
{
    public static StartComposerState Default { get; } = new();
}
