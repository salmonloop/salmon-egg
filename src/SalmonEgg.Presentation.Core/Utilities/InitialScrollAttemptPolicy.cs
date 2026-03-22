namespace SalmonEgg.Presentation.Utilities;

public enum InitialScrollAttemptOutcome
{
    Stop = 0,
    Retry = 1,
    Complete = 2,
}

public static class InitialScrollAttemptPolicy
{
    public static InitialScrollAttemptOutcome Decide(
        bool hasMessages,
        bool autoScrollEnabled,
        bool reachedBottom,
        int attempt,
        int maxAttempts)
    {
        if (!hasMessages || !autoScrollEnabled)
        {
            return InitialScrollAttemptOutcome.Stop;
        }

        if (reachedBottom)
        {
            return InitialScrollAttemptOutcome.Complete;
        }

        return attempt < maxAttempts
            ? InitialScrollAttemptOutcome.Retry
            : InitialScrollAttemptOutcome.Stop;
    }
}
