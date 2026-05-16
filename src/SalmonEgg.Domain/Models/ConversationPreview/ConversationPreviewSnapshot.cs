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
    DateTimeOffset Timestamp);
