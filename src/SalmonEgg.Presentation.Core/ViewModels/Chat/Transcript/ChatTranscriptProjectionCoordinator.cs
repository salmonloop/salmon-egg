using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.ConversationPreview;

namespace SalmonEgg.Presentation.ViewModels.Chat.Transcript;

internal sealed class ChatTranscriptProjectionCoordinator
{
    private const int MaxPreviewEntryCount = 24;
    private const int MaxPreviewTextLength = 1024;
    private readonly IConversationPreviewStore _previewStore;
    private readonly object _previewSnapshotSync = new();
    private string? _lastSavedPreviewConversationId;
    private IImmutableList<ConversationMessageSnapshot>? _lastSavedPreviewTranscript;

    public ChatTranscriptProjectionCoordinator(IConversationPreviewStore previewStore)
    {
        _previewStore = previewStore ?? throw new ArgumentNullException(nameof(previewStore));
    }

    public void ApplyProjection(
        ChatTranscriptProjectionContext context,
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript,
        bool sessionChanged)
    {
        ArgumentNullException.ThrowIfNull(context);

        ApplyTranscript(context, conversationId, transcript);
    }

    public ConversationPreviewSnapshot? BuildPreviewSnapshot(
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript,
        bool isHydrating)
    {
        if (isHydrating || transcript.Count == 0 || string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        var startIndex = Math.Max(0, transcript.Count - MaxPreviewEntryCount);
        var previewEntries = transcript
            .Skip(startIndex)
            .Select(m => new PreviewEntry(
                m.IsOutgoing ? "user" : "assistant",
                TrimPreviewText(m.TextContent),
                m.Timestamp))
            .ToArray();

        return new ConversationPreviewSnapshot(
            conversationId,
            previewEntries,
            DateTimeOffset.Now);
    }

    public ConversationPreviewSnapshot? PreparePreviewSnapshotSave(
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript,
        bool isHydrating)
    {
        var snapshot = BuildPreviewSnapshot(conversationId, transcript, isHydrating);
        if (snapshot is null)
        {
            return null;
        }

        lock (_previewSnapshotSync)
        {
            var previewTranscriptChanged =
                !string.Equals(_lastSavedPreviewConversationId, conversationId, StringComparison.Ordinal)
                || !ReferenceEquals(_lastSavedPreviewTranscript, transcript);
            if (!previewTranscriptChanged)
            {
                return null;
            }

            _lastSavedPreviewConversationId = conversationId;
            _lastSavedPreviewTranscript = transcript;
        }

        return snapshot;
    }

    public void SavePreviewSnapshot(ConversationPreviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _ = _previewStore.SaveAsync(snapshot);
    }

    public void UpdatePreviewSnapshot(
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript,
        bool isHydrating)
    {
        var snapshot = PreparePreviewSnapshotSave(conversationId, transcript, isHydrating);
        if (snapshot is null)
        {
            return;
        }

        SavePreviewSnapshot(snapshot);
    }

    private static string TrimPreviewText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= MaxPreviewTextLength
            ? text
            : text[..MaxPreviewTextLength];
    }

    private static void ApplyTranscript(
        ChatTranscriptProjectionContext context,
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript)
    {
        var history = context.GetMessageHistory();
        var previousCount = history.Count;
        var messages = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        if (history.CanApplyInPlace(conversationId, messages))
        {
            history.Reset(
                conversationId,
                messages,
                context.FromSnapshot,
                context.MatchesSnapshot);
        }
        else
        {
            history = ChatTranscriptVirtualizedMessageCollection.Create(
                conversationId,
                messages,
                context.FromSnapshot,
                context.MatchesSnapshot);
            context.SetMessageHistory(history);
        }

        var transcriptOwnerChanged = context.UpdateVisibleTranscriptConversationId(conversationId, history.Count > 0);
        if (previousCount != history.Count || transcriptOwnerChanged)
        {
            context.RaiseTranscriptStateChanged();
        }
    }
}
