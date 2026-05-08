using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.ViewModels.Chat.Transcript;

internal sealed class ChatTranscriptViewportProjectionCoordinator
{
    private readonly int _initialTailWindowSize;
    private readonly int _prependBatchSize;
    private string? _conversationId;
    private IImmutableList<ConversationMessageSnapshot> _activeTranscript = ImmutableList<ConversationMessageSnapshot>.Empty;
    private IReadOnlyList<ChatMessageViewModel> _activePreparedTranscript = Array.Empty<ChatMessageViewModel>();
    private bool _hasPreparedTranscript;

    public ChatTranscriptViewportProjectionCoordinator(int initialTailWindowSize = 120, int prependBatchSize = 80)
    {
        _initialTailWindowSize = Math.Max(1, initialTailWindowSize);
        _prependBatchSize = Math.Max(1, prependBatchSize);
    }

    public int WindowStartIndex { get; private set; }

    public int WindowCount { get; private set; }

    public void ActivateTranscript(
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript,
        ObservableCollection<ChatMessageViewModel> rendered,
        Func<ConversationMessageSnapshot, int, ChatMessageViewModel> projector)
    {
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(projector);

        _conversationId = conversationId;
        _activeTranscript = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        _activePreparedTranscript = Array.Empty<ChatMessageViewModel>();
        _hasPreparedTranscript = false;
        WindowCount = Math.Min(_initialTailWindowSize, _activeTranscript.Count);
        WindowStartIndex = Math.Max(0, _activeTranscript.Count - WindowCount);

        SyncRenderedWindow(rendered, index => projector(_activeTranscript[index], index));
    }

    public void ActivatePreparedTranscript(
        string? conversationId,
        IReadOnlyList<ChatMessageViewModel> preparedTranscript,
        ObservableCollection<ChatMessageViewModel> rendered)
    {
        ArgumentNullException.ThrowIfNull(preparedTranscript);
        ArgumentNullException.ThrowIfNull(rendered);

        _conversationId = conversationId;
        _activeTranscript = ImmutableList<ConversationMessageSnapshot>.Empty;
        _activePreparedTranscript = preparedTranscript;
        _hasPreparedTranscript = true;
        WindowCount = Math.Min(_initialTailWindowSize, preparedTranscript.Count);
        WindowStartIndex = Math.Max(0, preparedTranscript.Count - WindowCount);

        SyncRenderedWindow(rendered, index => preparedTranscript[index]);
    }

    public bool IsActiveForConversation(string? conversationId)
        => !string.IsNullOrWhiteSpace(_conversationId)
            && string.Equals(_conversationId, conversationId, StringComparison.Ordinal);

    public bool HasPartialWindowForConversation(string? conversationId)
        => IsActiveForConversation(conversationId)
            && WindowCount > 0
            && WindowCount < GetTotalCount();

    public bool SyncTranscript(
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript,
        ObservableCollection<ChatMessageViewModel> rendered,
        Func<ConversationMessageSnapshot, int, ChatMessageViewModel> projector,
        Func<ChatMessageViewModel, ConversationMessageSnapshot, bool> matchesSnapshot,
        bool isTailAnchored)
    {
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(matchesSnapshot);

        if (!IsActiveForConversation(conversationId))
        {
            return false;
        }

        _activeTranscript = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        _activePreparedTranscript = Array.Empty<ChatMessageViewModel>();
        _hasPreparedTranscript = false;
        RecalculateWindowBounds(isTailAnchored);
        return SyncRenderedTranscriptWindow(rendered, projector, matchesSnapshot);
    }

    public bool SyncPreparedTranscript(
        string? conversationId,
        IReadOnlyList<ChatMessageViewModel> preparedTranscript,
        ObservableCollection<ChatMessageViewModel> rendered)
    {
        ArgumentNullException.ThrowIfNull(preparedTranscript);
        ArgumentNullException.ThrowIfNull(rendered);

        if (!IsActiveForConversation(conversationId))
        {
            return false;
        }

        _activeTranscript = ImmutableList<ConversationMessageSnapshot>.Empty;
        _activePreparedTranscript = preparedTranscript;
        _hasPreparedTranscript = true;
        RecalculateWindowBounds(tailAnchored: true);
        return SyncRenderedWindow(rendered, index => preparedTranscript[index]);
    }

    public bool ExpandOlderWindow(
        ObservableCollection<ChatMessageViewModel> rendered,
        Func<ConversationMessageSnapshot, int, ChatMessageViewModel> projector)
    {
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(projector);

        if (WindowStartIndex == 0)
        {
            return false;
        }

        var batchStart = Math.Max(0, WindowStartIndex - _prependBatchSize);
        for (var i = WindowStartIndex - 1; i >= batchStart; i--)
        {
            rendered.Insert(0, ResolveProjectedMessage(i, projector));
        }

        WindowCount += WindowStartIndex - batchStart;
        WindowStartIndex = batchStart;
        return true;
    }

    public bool MaterializeWindowAroundProjectionItem(
        string projectionItemKey,
        ObservableCollection<ChatMessageViewModel> rendered,
        Func<ConversationMessageSnapshot, int, ChatMessageViewModel> projector,
        Func<ConversationMessageSnapshot, int, string> getProjectionItemKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionItemKey);
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(getProjectionItemKey);

        if (_hasPreparedTranscript)
        {
            return MaterializePreparedWindowAroundProjectionItem(projectionItemKey, rendered);
        }

        var targetIndex = -1;
        for (var i = 0; i < _activeTranscript.Count; i++)
        {
            if (string.Equals(getProjectionItemKey(_activeTranscript[i], i), projectionItemKey, StringComparison.Ordinal))
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
        {
            return false;
        }

        MoveWindowAroundIndex(targetIndex);
        SyncRenderedWindow(rendered, index => projector(_activeTranscript[index], index));
        return true;
    }

    private bool MaterializePreparedWindowAroundProjectionItem(
        string projectionItemKey,
        ObservableCollection<ChatMessageViewModel> rendered)
    {
        var targetIndex = -1;
        for (var i = 0; i < _activePreparedTranscript.Count; i++)
        {
            if (string.Equals(_activePreparedTranscript[i].ProjectionItemKey, projectionItemKey, StringComparison.Ordinal))
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
        {
            return false;
        }

        MoveWindowAroundIndex(targetIndex);
        SyncRenderedWindow(rendered, index => _activePreparedTranscript[index]);
        return true;
    }

    private void MoveWindowAroundIndex(int targetIndex)
    {
        var totalCount = GetTotalCount();
        WindowCount = Math.Min(_initialTailWindowSize, totalCount);
        var halfWindow = WindowCount / 2;
        var maxStartIndex = Math.Max(0, totalCount - WindowCount);
        WindowStartIndex = Math.Clamp(targetIndex - halfWindow, 0, maxStartIndex);
    }

    private void RecalculateWindowBounds(bool tailAnchored)
    {
        var totalCount = GetTotalCount();
        if (totalCount == 0)
        {
            WindowStartIndex = 0;
            WindowCount = 0;
            return;
        }

        WindowCount = Math.Max(1, Math.Min(WindowCount, totalCount));
        WindowStartIndex = tailAnchored
            ? Math.Max(0, totalCount - WindowCount)
            : Math.Min(WindowStartIndex, Math.Max(0, totalCount - WindowCount));
    }

    private int GetTotalCount()
        => _hasPreparedTranscript ? _activePreparedTranscript.Count : _activeTranscript.Count;

    private bool SyncRenderedWindow(
        ObservableCollection<ChatMessageViewModel> rendered,
        Func<int, ChatMessageViewModel> resolveMessage)
    {
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(resolveMessage);

        var changed = false;
        for (var offset = 0; offset < WindowCount; offset++)
        {
            var index = WindowStartIndex + offset;
            var projectedMessage = resolveMessage(index);
            if (offset < rendered.Count)
            {
                if (!ReferenceEquals(rendered[offset], projectedMessage))
                {
                    rendered[offset] = projectedMessage;
                    changed = true;
                }
            }
            else
            {
                rendered.Add(projectedMessage);
                changed = true;
            }
        }

        while (rendered.Count > WindowCount)
        {
            rendered.RemoveAt(rendered.Count - 1);
            changed = true;
        }

        return changed;
    }

    private bool SyncRenderedTranscriptWindow(
        ObservableCollection<ChatMessageViewModel> rendered,
        Func<ConversationMessageSnapshot, int, ChatMessageViewModel> projector,
        Func<ChatMessageViewModel, ConversationMessageSnapshot, bool> matchesSnapshot)
    {
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(matchesSnapshot);

        var changed = false;
        for (var offset = 0; offset < WindowCount; offset++)
        {
            var index = WindowStartIndex + offset;
            var snapshot = _activeTranscript[index];
            if (offset < rendered.Count)
            {
                if (matchesSnapshot(rendered[offset], snapshot))
                {
                    continue;
                }

                rendered[offset] = projector(snapshot, index);
                changed = true;
            }
            else
            {
                rendered.Add(projector(snapshot, index));
                changed = true;
            }
        }

        while (rendered.Count > WindowCount)
        {
            rendered.RemoveAt(rendered.Count - 1);
            changed = true;
        }

        return changed;
    }

    private ChatMessageViewModel ResolveProjectedMessage(
        int index,
        Func<ConversationMessageSnapshot, int, ChatMessageViewModel> projector)
    {
        if (_hasPreparedTranscript)
        {
            return _activePreparedTranscript[index];
        }

        return projector(_activeTranscript[index], index);
    }
}
