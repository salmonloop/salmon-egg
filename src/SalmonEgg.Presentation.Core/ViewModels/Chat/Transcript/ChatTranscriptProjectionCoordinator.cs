using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.ConversationPreview;

namespace SalmonEgg.Presentation.ViewModels.Chat.Transcript;

internal sealed class ChatTranscriptProjectionCoordinator
{
    private const int LargeTranscriptReplacementThreshold = 64;

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
        IReadOnlyList<ChatMessageViewModel>? preparedTranscript,
        bool sessionChanged)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (sessionChanged)
        {
            if (preparedTranscript is { Count: > 0 })
            {
                ReplaceMessageHistory(context, conversationId, preparedTranscript);
            }
            else
            {
                ReplaceMessageHistory(context, conversationId, transcript);
            }

            return;
        }

        if (ShouldReplaceMessageHistory(context, transcript))
        {
            ReplaceMessageHistory(context, conversationId, transcript);
            return;
        }

        SyncMessageHistory(context, conversationId, transcript);
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

        var previewEntries = transcript
            .Select(m => new PreviewEntry(
                m.IsOutgoing ? "user" : "assistant",
                m.TextContent ?? string.Empty,
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

    private static void SyncMessageHistory(
        ChatTranscriptProjectionContext context,
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript)
    {
        var history = context.GetMessageHistory();
        var previousCount = history.Count;
        var messages = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (i < history.Count)
            {
                if (history[i].Id != message.Id)
                {
                    while (history.Count > i)
                    {
                        history.RemoveAt(i);
                    }

                    history.Add(context.FromSnapshot(message, i));
                }
                else if (!context.MatchesSnapshot(history[i], message))
                {
                    history[i] = context.FromSnapshot(message, i);
                }
            }
            else
            {
                history.Add(context.FromSnapshot(message, i));
            }
        }

        while (history.Count > messages.Count)
        {
            history.RemoveAt(history.Count - 1);
        }

        var transcriptOwnerChanged = context.UpdateVisibleTranscriptConversationId(conversationId, history.Count > 0);
        if (previousCount != history.Count || transcriptOwnerChanged)
        {
            context.RaiseTranscriptStateChanged();
        }
    }

    private static void ReplaceMessageHistory(
        ChatTranscriptProjectionContext context,
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript)
    {
        var messages = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var history = new ObservableCollection<ChatMessageViewModel>(
            messages.Select((message, index) => context.FromSnapshot(message, index)));
        context.SetMessageHistory(history);
        context.UpdateVisibleTranscriptConversationId(conversationId, history.Count > 0);
        context.RaiseTranscriptStateChanged();
    }

    private static void ReplaceMessageHistory(
        ChatTranscriptProjectionContext context,
        string? conversationId,
        IReadOnlyList<ChatMessageViewModel> transcript)
    {
        var messages = transcript ?? Array.Empty<ChatMessageViewModel>();
        var history = new ObservableCollection<ChatMessageViewModel>(messages);
        context.SetMessageHistory(history);
        context.UpdateVisibleTranscriptConversationId(conversationId, history.Count > 0);
        context.RaiseTranscriptStateChanged();
    }

    private static bool ShouldReplaceMessageHistory(
        ChatTranscriptProjectionContext context,
        IImmutableList<ConversationMessageSnapshot> transcript)
    {
        var history = context.GetMessageHistory();
        var messages = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        if (history.Count == 0 || messages.Count == 0)
        {
            return false;
        }

        var appendedMessageCount = messages.Count - history.Count;
        if (appendedMessageCount < LargeTranscriptReplacementThreshold)
        {
            return false;
        }

        if (history.Count > messages.Count)
        {
            return false;
        }

        for (int i = 0; i < history.Count; i++)
        {
            if (!context.MatchesSnapshot(history[i], messages[i]))
            {
                return false;
            }
        }

        return true;
    }
}
