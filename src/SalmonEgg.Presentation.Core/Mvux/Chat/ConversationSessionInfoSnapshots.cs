using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Utilities;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

internal static class ConversationSessionInfoSnapshots
{
    public static ConversationSessionInfoSnapshot? Clone(ConversationSessionInfoSnapshot? sessionInfo)
    {
        if (sessionInfo is null)
        {
            return null;
        }

        return new ConversationSessionInfoSnapshot
        {
            Title = sessionInfo.Title,
            HasTitle = sessionInfo.HasTitle,
            Description = sessionInfo.Description,
            Cwd = sessionInfo.Cwd,
            UpdatedAtUtc = sessionInfo.UpdatedAtUtc,
            HasUpdatedAt = sessionInfo.HasUpdatedAt,
            Meta = sessionInfo.Meta is null
                ? null
                : new Dictionary<string, object?>(sessionInfo.Meta, StringComparer.Ordinal)
        };
    }

    public static ConversationSessionInfoSnapshot Merge(
        ConversationSessionInfoSnapshot? existing,
        ConversationSessionInfoSnapshot incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        var mergedMeta = existing?.Meta is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(existing.Meta, StringComparer.Ordinal);
        if (incoming.Meta is not null)
        {
            foreach (var pair in incoming.Meta)
            {
                mergedMeta[pair.Key] = pair.Value;
            }
        }

        return new ConversationSessionInfoSnapshot
        {
            Title = incoming.HasTitle ? incoming.Title : existing?.Title,
            HasTitle = incoming.HasTitle || existing?.HasTitle == true,
            Description = ResolveIncomingField(incoming.Description, existing?.Description),
            Cwd = ResolveIncomingField(incoming.Cwd, existing?.Cwd),
            UpdatedAtUtc = ResolveIncomingUpdatedAt(existing?.UpdatedAtUtc, incoming),
            HasUpdatedAt = incoming.HasUpdatedAt || existing?.HasUpdatedAt == true,
            Meta = mergedMeta.Count == 0 ? null : mergedMeta
        };
    }

    private static string? ResolveIncomingField(string? incoming, string? existing)
        => !string.IsNullOrWhiteSpace(incoming) ? incoming : existing;

    private static DateTime? ResolveIncomingUpdatedAt(
        DateTime? existing,
        ConversationSessionInfoSnapshot incoming)
    {
        if (!incoming.HasUpdatedAt)
        {
            return existing;
        }

        if (incoming.UpdatedAtUtc is not DateTime incomingValue || incomingValue == default)
        {
            return null;
        }

        return AcpSessionTimestampPolicy.ResolveLatestUpdatedAtUtc(existing, incomingValue);
    }
}
