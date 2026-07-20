using System;
using System.Collections.Generic;
using SalmonEgg.Acp.Protocol;
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
            Cwd = sessionInfo.Cwd,
            AdditionalDirectories = CloneAdditionalDirectories(sessionInfo.AdditionalDirectories),
            UpdatedAtUtc = sessionInfo.UpdatedAtUtc,
            HasUpdatedAt = sessionInfo.HasUpdatedAt,
            Meta = AcpMetaDictionaryJsonConverter.Clone(sessionInfo.Meta)
        };
    }

    public static ConversationSessionInfoSnapshot Merge(
        ConversationSessionInfoSnapshot? existing,
        ConversationSessionInfoSnapshot incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        var mergedMeta = AcpMetaDictionaryJsonConverter.Clone(existing?.Meta)
            ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        if (incoming.Meta is not null)
        {
            foreach (var pair in AcpMetaDictionaryJsonConverter.Clone(incoming.Meta)!)
            {
                mergedMeta[pair.Key] = pair.Value;
            }
        }

        return new ConversationSessionInfoSnapshot
        {
            Title = incoming.HasTitle ? incoming.Title : existing?.Title,
            HasTitle = incoming.HasTitle || existing?.HasTitle == true,
            Cwd = ResolveIncomingField(incoming.Cwd, existing?.Cwd),
            AdditionalDirectories = incoming.AdditionalDirectories is null
                ? CloneAdditionalDirectories(existing?.AdditionalDirectories)
                : new List<string>(incoming.AdditionalDirectories),
            UpdatedAtUtc = ResolveIncomingUpdatedAt(existing?.UpdatedAtUtc, incoming),
            HasUpdatedAt = incoming.HasUpdatedAt || existing?.HasUpdatedAt == true,
            Meta = mergedMeta.Count == 0 ? null : mergedMeta
        };
    }

    private static string? ResolveIncomingField(string? incoming, string? existing)
        => !string.IsNullOrWhiteSpace(incoming) ? incoming : existing;

    private static List<string>? CloneAdditionalDirectories(IReadOnlyCollection<string>? directories)
        => directories is null ? null : new List<string>(directories);

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
