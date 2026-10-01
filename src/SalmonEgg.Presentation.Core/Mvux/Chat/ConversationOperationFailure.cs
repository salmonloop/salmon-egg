using System;
using System.Collections.Immutable;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public sealed record ConversationOperationFailure(
    string? ConversationId,
    string Message,
    string? ResourceKey = null,
    string? Fallback = null,
    ImmutableArray<object> FormatArgs = default)
{
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}
