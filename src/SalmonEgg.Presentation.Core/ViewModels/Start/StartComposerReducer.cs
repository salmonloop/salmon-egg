namespace SalmonEgg.Presentation.ViewModels.Start;

public static class StartComposerReducer
{
    public static StartComposerState Reduce(StartComposerState state, StartComposerAction action)
        => action switch
        {
            Loaded => StartComposerState.Default,
            Unloaded => state with
            {
                HasFocusWithin = false,
                IsPopupOpen = false,
                IsSubmitting = false,
                WasExplicitlyActivated = false,
            },
            Activated => state with
            {
                WasExplicitlyActivated = true,
            },
            FocusEntered => state with
            {
                HasFocusWithin = true,
                WasExplicitlyActivated = true,
            },
            FocusExited => state with
            {
                HasFocusWithin = false,
            },
            PopupOpened => state with
            {
                IsPopupOpen = true,
                WasExplicitlyActivated = true,
            },
            PopupClosed => state with
            {
                IsPopupOpen = false,
            },
            DraftChanged draftChanged => state with
            {
                HasDraft = draftChanged.HasDraft,
                WasExplicitlyActivated = state.WasExplicitlyActivated || draftChanged.HasDraft,
            },
            SuggestionApplied => state with
            {
                WasExplicitlyActivated = true,
            },
            OutsidePointerPressed when state.IsPopupOpen || state.IsSubmitting => state,
            OutsidePointerPressed when state.HasDraft => state with
            {
                HasFocusWithin = false,
                WasExplicitlyActivated = true,
            },
            OutsidePointerPressed => state with
            {
                HasFocusWithin = false,
                WasExplicitlyActivated = false,
                IsPopupOpen = false,
            },
            SubmitStarted => state with
            {
                HasFocusWithin = false,
                IsSubmitting = true,
                IsPopupOpen = false,
                WasExplicitlyActivated = true,
            },
            SubmitCompleted => state with
            {
                HasFocusWithin = false,
                IsSubmitting = false,
                IsPopupOpen = false,
                WasExplicitlyActivated = state.WasExplicitlyActivated,
            },
            _ => state,
        };
}
