namespace SalmonEgg.Presentation.Utilities;

public sealed class TranscriptAutoFollowController
{
    public bool IsAutoFollowEnabled { get; private set; } = true;

    public bool HasPendingManualViewportEvaluation { get; private set; }

    public void Reset()
    {
        IsAutoFollowEnabled = true;
        HasPendingManualViewportEvaluation = false;
    }

    public void RegisterManualViewportIntent(bool hasMessages)
    {
        if (!hasMessages)
        {
            HasPendingManualViewportEvaluation = false;
            return;
        }

        IsAutoFollowEnabled = false;
        HasPendingManualViewportEvaluation = true;
    }

    public bool ResolveManualViewportState(bool isViewportAtBottom)
    {
        if (!HasPendingManualViewportEvaluation)
        {
            return false;
        }

        IsAutoFollowEnabled = isViewportAtBottom;
        HasPendingManualViewportEvaluation = false;
        return true;
    }

    public bool ShouldRecoverBottom(
        bool isSessionActive,
        bool hasMessages,
        bool hasPendingInitialScroll,
        bool isProgrammaticScrollInFlight,
        bool isViewportAtBottom)
    {
        if (!IsAutoFollowEnabled
            || !isSessionActive
            || !hasMessages
            || hasPendingInitialScroll
            || isProgrammaticScrollInFlight)
        {
            return false;
        }

        return !isViewportAtBottom;
    }
}
