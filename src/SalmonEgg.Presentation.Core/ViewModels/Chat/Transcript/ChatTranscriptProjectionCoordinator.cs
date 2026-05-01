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
    private readonly IConversationPreviewStore _previewStore;
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

        SyncMessageHistory(context, conversationId, transcript);
    }

    public void UpdatePreviewSnapshot(
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript,
        bool isHydrating)
    {
        if (isHydrating || transcript.Count == 0 || string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var previewTranscriptChanged =
            !string.Equals(_lastSavedPreviewConversationId, conversationId, StringComparison.Ordinal)
            || !ReferenceEquals(_lastSavedPreviewTranscript, transcript);

        if (!previewTranscriptChanged)
        {
            return;
        }

        var previewEntries = transcript
            .Select(m => new PreviewEntry(
                m.IsOutgoing ? "user" : "assistant",
                m.TextContent ?? string.Empty,
                m.Timestamp))
            .ToArray();

        var snapshotToSave = new ConversationPreviewSnapshot(
            conversationId,
            previewEntries,
            DateTimeOffset.Now);

        _lastSavedPreviewConversationId = conversationId;
        _lastSavedPreviewTranscript = transcript;
        _ = _previewStore.SaveAsync(snapshotToSave);
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

                    history.Add(context.FromSnapshot(message));
                }
                else if (!context.MatchesSnapshot(history[i], message))
                {
                    history[i] = context.FromSnapshot(message);
                }
            }
            else
            {
                history.Add(context.FromSnapshot(message));
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
        var history = new ObservableCollection<ChatMessageViewModel>(messages.Select(context.FromSnapshot));
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
}
