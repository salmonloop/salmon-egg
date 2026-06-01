using System;

namespace SalmonEgg.Presentation.Core.Services.Input;

public static class VoiceInputErrorMessageSanitizer
{
    public static string Normalize(string? message, string fallback)
    {
        var candidate = message?.Trim();
        if (string.IsNullOrWhiteSpace(candidate)
            || LooksLikeSystemPlaceholder(candidate))
        {
            return fallback;
        }

        return candidate;
    }

    private static bool LooksLikeSystemPlaceholder(string message)
    {
        return message.IndexOf("没有与此错误关联的文本", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("No text is associated with this error", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
