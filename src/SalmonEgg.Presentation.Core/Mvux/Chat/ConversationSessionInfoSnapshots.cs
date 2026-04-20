using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models.Conversation;

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
            Description = sessionInfo.Description,
            Cwd = sessionInfo.Cwd,
            UpdatedAtUtc = sessionInfo.UpdatedAtUtc,
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
            Title = ResolveIncomingField(incoming.Title, existing?.Title),
            Description = ResolveIncomingField(incoming.Description, existing?.Description),
            Cwd = ResolveIncomingField(incoming.Cwd, existing?.Cwd),
            UpdatedAtUtc = incoming.UpdatedAtUtc ?? existing?.UpdatedAtUtc,
            Meta = mergedMeta.Count == 0 ? null : mergedMeta
        };
    }

    private static string? ResolveIncomingField(string? incoming, string? existing)
        => !string.IsNullOrWhiteSpace(incoming) ? incoming : existing;
}
