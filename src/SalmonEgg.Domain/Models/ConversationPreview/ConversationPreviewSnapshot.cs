using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.ConversationPreview;

public record ConversationPreviewSnapshot(
    string ConversationId,
    IReadOnlyList<PreviewEntry> Entries,
    DateTimeOffset GeneratedAt);

public record PreviewEntry(
    string Sender,
    string Text,
    // Mirrors ConversationMessageSnapshot.Timestamp: null when the source message had no
    // authoritative time (ACP replay/chunks). The cache never synthesizes a clock.
    DateTimeOffset? Timestamp);
