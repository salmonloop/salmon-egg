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
        if (HasPendingManualViewportEvaluation)
        {
            if (isViewportAtBottom)
            {
                // The first layout tick after pointer/wheel input can still be sitting
                // at bottom before the user's manual scroll has actually moved the viewport.
                // Keep auto-follow detached until we observe a real viewport change.
                IsAutoFollowEnabled = false;
                return false;
            }

            HasPendingManualViewportEvaluation = false;
            IsAutoFollowEnabled = false;
            return true;
        }

        if (!IsAutoFollowEnabled && isViewportAtBottom)
        {
            IsAutoFollowEnabled = true;
            return true;
        }

        return false;
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
