namespace SalmonEgg.Presentation.ViewModels.Start;

public static class StartSessionModePolicy
{
    public static StartSessionModeSnapshot Compute(StartSessionModeState state)
    {
        var stage = ResolveStage(state);
        var isEnabled = stage == StartSessionModeStage.Ready;
        var canSubmitPrompt =
            !state.IsStarting
            && !state.IsDraftRefreshPending
            && !state.IsDraftLoading
            && state.IsConnectionReady
            && state.IsDraftReady;

        return new StartSessionModeSnapshot(stage, isEnabled, canSubmitPrompt);
    }

    private static StartSessionModeStage ResolveStage(StartSessionModeState state)
    {
        if (state.IsStarting)
        {
            return StartSessionModeStage.Submitting;
        }

        if (state.IsConnectionInProgress || state.IsDraftRefreshPending || state.IsDraftLoading)
        {
            return StartSessionModeStage.Loading;
        }

        if (state.IsConnectionReady && state.IsDraftReady && state.ModeCount > 0)
        {
            return StartSessionModeStage.Ready;
        }

        return StartSessionModeStage.Unavailable;
    }
}
