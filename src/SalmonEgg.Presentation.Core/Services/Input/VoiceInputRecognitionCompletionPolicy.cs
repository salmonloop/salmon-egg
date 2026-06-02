using System;

namespace SalmonEgg.Presentation.Core.Services.Input;

public static class VoiceInputRecognitionCompletionPolicy
{
    public static bool ShouldTreatAsGracefulEnd(
        string? completionStatus,
        bool stopRequested,
        int partialResultCount,
        int finalResultCount)
    {
        if (stopRequested)
        {
            return true;
        }

        if (string.Equals(completionStatus, "Success", StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(completionStatus, "UserCanceled", StringComparison.Ordinal)
            && (partialResultCount > 0 || finalResultCount > 0);
    }

    public static string BuildFailureMessage(string? completionStatus)
    {
        if (string.Equals(completionStatus, "UserCanceled", StringComparison.Ordinal))
        {
            return "Voice input was interrupted before any recognition result was produced.";
        }

        return $"Voice recognition ended: {completionStatus}.";
    }
}
