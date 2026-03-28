namespace SalmonEgg.Presentation.ViewModels.Start;

public static class StartComposerPolicy
{
    public static StartComposerSnapshot Compute(StartComposerState state)
    {
        var stage = ResolveStage(state);
        var isExpanded = stage != StartComposerStage.Collapsed;

        return new StartComposerSnapshot(
            Stage: stage,
            IsExpanded: isExpanded,
            ShowHeroSuggestions: stage == StartComposerStage.Collapsed,
            ShowPreflightSuggestions: isExpanded && stage != StartComposerStage.Submitting,
            ShowHeroChrome: true,
            FreezeComposerInteractions: stage == StartComposerStage.Submitting);
    }

    private static StartComposerStage ResolveStage(StartComposerState state)
    {
        if (state.IsSubmitting)
        {
            return StartComposerStage.Submitting;
        }

        if (state.IsPopupOpen)
        {
            return StartComposerStage.PopupEngaged;
        }

        if (state.HasFocusWithin && state.HasDraft)
        {
            return StartComposerStage.Interacting;
        }

        if (state.HasFocusWithin)
        {
            return StartComposerStage.Primed;
        }

        if (state.HasDraft)
        {
            return StartComposerStage.ExpandedIdle;
        }

        return StartComposerStage.Collapsed;
    }
}
