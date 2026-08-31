using System;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Authoritative timestamp contract for conversation messages.
/// Snapshots store protocol/local-observed instants as UTC (or unspecified-as-UTC ticks);
/// ViewModels project display local time. Equality must compare instants, not ambient Kind.
/// </summary>
public static class ConversationMessageTimestamp
{
    public static DateTime? ToDisplayLocal(DateTime? authoritative)
        => authoritative is null ? null : NormalizeToUtc(authoritative.Value).ToLocalTime();

    public static DateTime? ToAuthoritativeUtc(DateTime? value)
        => value is null ? null : NormalizeToUtc(value.Value);

    public static bool InstantEquals(DateTime? left, DateTime? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return NormalizeToUtc(left.Value) == NormalizeToUtc(right.Value);
    }

    private static DateTime NormalizeToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // Persistence / ACP snapshots treat unspecified ticks as UTC.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
