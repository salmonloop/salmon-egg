namespace SalmonEgg.Presentation.Utilities;

public static class InitialLayoutLoadingPolicy
{
    public static bool ShouldKeepLoading(
        bool isSessionActive,
        int messageCount,
        bool hasPendingInitialScroll,
        bool lastItemContainerGenerated,
        bool isHydrating,
        bool isRemoteHydrationPending)
    {
        if (!isSessionActive)
        {
            return false;
        }

        // Keep loading while we're hydrating remote state, even if messageCount is 0.
        // This prevents the "flash of empty chat interface" when switching sessions.
        if (isHydrating || isRemoteHydrationPending)
        {
            return true;
        }

        if (messageCount <= 0)
        {
            return false;
        }

        if (lastItemContainerGenerated)
        {
            return false;
        }

        return hasPendingInitialScroll;
    }
}
