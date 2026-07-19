namespace SalmonEgg.Presentation.Utilities;

public static class InitialLayoutLoadingPolicy
{
    public static bool ShouldKeepLoading(
        bool isSessionActive,
        bool isHydrating,
        bool isRemoteHydrationPending)
    {
        if (!isSessionActive)
        {
            return false;
        }

        // Keep loading while we're hydrating remote state, even if the transcript is empty.
        // This prevents the "flash of empty chat interface" when switching sessions.
        return isHydrating || isRemoteHydrationPending;
    }
}
