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
    private readonly ChatTranscriptViewportProjectionCoordinator _viewportProjectionCoordinator;
    private readonly object _previewSnapshotSync = new();
    private string? _lastSavedPreviewConversationId;
    private IImmutableList<ConversationMessageSnapshot>? _lastSavedPreviewTranscript;

    public ChatTranscriptProjectionCoordinator(
        IConversationPreviewStore previewStore,
        ChatTranscriptViewportProjectionCoordinator? viewportProjectionCoordinator = null)
    {
        _previewStore = previewStore ?? throw new ArgumentNullException(nameof(previewStore));
        _viewportProjectionCoordinator = viewportProjectionCoordinator ?? new ChatTranscriptViewportProjectionCoordinator();
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
            var history = context.GetMessageHistory();
            if (preparedTranscript is { Count: > 0 })
            {
                _viewportProjectionCoordinator.ActivatePreparedTranscript(
                    conversationId,
                    preparedTranscript,
                    history);
            }
            else
            {
                _viewportProjectionCoordinator.ActivateTranscript(
                    conversationId,
                    transcript,
                    history,
                    context.FromSnapshot);
            }

            context.UpdateVisibleTranscriptConversationId(conversationId, history.Count > 0);
            context.RaiseTranscriptStateChanged();
            return;
        }

        if (_viewportProjectionCoordinator.HasPartialWindowForConversation(conversationId))
        {
            var history = context.GetMessageHistory();
            var changed = preparedTranscript is { Count: > 0 }
                ? _viewportProjectionCoordinator.SyncPreparedTranscript(conversationId, preparedTranscript, history)
                : _viewportProjectionCoordinator.SyncTranscript(
                    conversationId,
                    transcript,
                    history,
                    context.FromSnapshot,
                    context.MatchesSnapshot,
                    context.IsTailAnchored());
            var transcriptOwnerChanged = context.UpdateVisibleTranscriptConversationId(conversationId, history.Count > 0);
            if (changed || transcriptOwnerChanged)
            {
                context.RaiseTranscriptStateChanged();
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

    public bool TryExpandOlderWindow(
        ChatTranscriptProjectionContext context,
        string? conversationId)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_viewportProjectionCoordinator.IsActiveForConversation(conversationId))
        {
            return false;
        }

        var history = context.GetMessageHistory();
        var expanded = _viewportProjectionCoordinator.ExpandOlderWindow(history, context.FromSnapshot);
        if (!expanded)
        {
            return false;
        }

        context.UpdateVisibleTranscriptConversationId(conversationId, history.Count > 0);
        context.RaiseTranscriptStateChanged();
        return true;
    }

    public bool TryMaterializeProjectionItem(
        ChatTranscriptProjectionContext context,
        string? conversationId,
        string? projectionItemKey)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(projectionItemKey)
            || !_viewportProjectionCoordinator.IsActiveForConversation(conversationId))
        {
            return false;
        }

        var history = context.GetMessageHistory();
        var materialized = _viewportProjectionCoordinator.MaterializeWindowAroundProjectionItem(
            projectionItemKey,
            history,
            context.FromSnapshot,
            context.GetProjectionItemKey);
        if (!materialized)
        {
            return false;
        }

        context.UpdateVisibleTranscriptConversationId(conversationId, history.Count > 0);
        context.RaiseTranscriptStateChanged();
        return true;
    }

    private static void SyncMessageHistory(
        ChatTranscriptProjectionContext context,
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript)
    {
        var history = context.GetMessageHistory();
        var previousCount = history.Count;
        var changed = false;
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
                    changed = true;
                }
                else if (!context.MatchesSnapshot(history[i], message))
                {
                    history[i] = context.FromSnapshot(message, i);
                    changed = true;
                }
            }
            else
            {
                history.Add(context.FromSnapshot(message, i));
                changed = true;
            }
        }

        while (history.Count > messages.Count)
        {
            history.RemoveAt(history.Count - 1);
            changed = true;
        }

        var transcriptOwnerChanged = context.UpdateVisibleTranscriptConversationId(conversationId, history.Count > 0);
        if (changed || previousCount != history.Count || transcriptOwnerChanged)
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
