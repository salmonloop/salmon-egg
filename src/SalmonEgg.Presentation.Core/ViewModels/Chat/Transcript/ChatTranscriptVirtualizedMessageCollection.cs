using System.Collections;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Chat.Transcript;

public readonly record struct TranscriptVirtualizationRange(int FirstIndex, int Length)
{
    public int LastIndex
    {
        get
        {
            if (Length <= 0)
            {
                return FirstIndex;
            }

            var maximumFirstIndexWithoutOverflow = int.MaxValue - Length + 1;
            return FirstIndex > maximumFirstIndexWithoutOverflow
                ? int.MaxValue
                : FirstIndex + Length - 1;
        }
    }

    public bool Contains(int index) => index >= FirstIndex && index <= LastIndex;
}

public sealed class ChatTranscriptVirtualizedMessageCollection :
    IList<ChatMessageViewModel>,
    IList,
    IReadOnlyList<ChatMessageViewModel>,
    INotifyCollectionChanged,
    INotifyPropertyChanged
{
    private IImmutableList<ConversationMessageSnapshot> _transcript = ImmutableList<ConversationMessageSnapshot>.Empty;
    private readonly Dictionary<int, ChatMessageViewModel> _cache = new();
    private Func<ConversationMessageSnapshot, int, ChatMessageViewModel>? _projector;
    private Func<ChatMessageViewModel, ConversationMessageSnapshot, bool>? _matchesSnapshot;
    private string? _conversationId;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Count => _transcript.Count;

    public bool IsReadOnly => true;

    public bool IsFixedSize => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    public ChatMessageViewModel this[int index]
    {
        get
        {
            if (index < 0 || index >= _transcript.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (_cache.TryGetValue(index, out var message))
            {
                return message;
            }

            var projected = CreateItem(index);
            _cache[index] = projected;
            return projected;
        }
        set => throw new NotSupportedException();
    }

    public void Reset(
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript,
        Func<ConversationMessageSnapshot, int, ChatMessageViewModel> projector,
        Func<ChatMessageViewModel, ConversationMessageSnapshot, bool> matchesSnapshot)
    {
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(matchesSnapshot);

        var messages = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var oldCount = _transcript.Count;
        var oldTranscript = _transcript;
        var sameConversation = string.Equals(_conversationId, conversationId, StringComparison.Ordinal);
        var unchangedTranscript = sameConversation && ReferenceEquals(oldTranscript, messages);
        var shouldAppend = CanPublishAppend(oldTranscript, messages, sameConversation);

        _conversationId = conversationId;
        _transcript = messages;
        _projector = projector;
        _matchesSnapshot = matchesSnapshot;

        if (unchangedTranscript)
        {
            return;
        }

        PruneChangedCachedItems(oldTranscript, messages, sameConversation);
        if (shouldAppend)
        {
            RaiseAppend(oldCount, messages.Count - oldCount);
        }
        else
        {
            RaiseReset();
        }

        if (oldCount != messages.Count)
        {
            OnPropertyChanged(nameof(Count));
        }
    }

    public int IndexOf(ChatMessageViewModel item)
    {
        if (item is null)
        {
            return -1;
        }

        foreach (var entry in _cache)
        {
            if (ReferenceEquals(entry.Value, item))
            {
                return entry.Key;
            }
        }

        if (!string.IsNullOrWhiteSpace(item.ProjectionItemKey))
        {
            for (var index = 0; index < _transcript.Count; index++)
            {
                if (string.Equals(
                        item.ProjectionItemKey,
                        TranscriptProjectionRestoreTokenProjector.CreateProjectionItemKey(_transcript[index], index),
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        return -1;
    }

    public bool Contains(ChatMessageViewModel item) => IndexOf(item) >= 0;

    public void CopyTo(ChatMessageViewModel[] array, int arrayIndex)
    {
        for (var index = 0; index < _transcript.Count; index++)
        {
            array[arrayIndex + index] = this[index];
        }
    }

    public IEnumerator<ChatMessageViewModel> GetEnumerator()
    {
        for (var index = 0; index < _transcript.Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Insert(int index, ChatMessageViewModel item) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public void Add(ChatMessageViewModel item) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Remove(ChatMessageViewModel item) => throw new NotSupportedException();

    int IList.Add(object? value) => throw new NotSupportedException();

    bool IList.Contains(object? value) => value is ChatMessageViewModel message && Contains(message);

    int IList.IndexOf(object? value) => value is ChatMessageViewModel message ? IndexOf(message) : -1;

    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    void IList.Remove(object? value) => throw new NotSupportedException();

    public void CopyTo(Array array, int index)
    {
        for (var itemIndex = 0; itemIndex < _transcript.Count; itemIndex++)
        {
            array.SetValue(this[itemIndex], index + itemIndex);
        }
    }

    public ChatMessageViewModel CreateItem(int index)
    {
        if (index < 0 || index >= _transcript.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (_projector is null)
        {
            throw new InvalidOperationException("Transcript projector has not been initialized.");
        }

        return _projector(_transcript[index], index);
    }

    public bool MatchesItem(int index, ChatMessageViewModel item)
    {
        if (index < 0 || index >= _transcript.Count || _matchesSnapshot is null)
        {
            return false;
        }

        return _matchesSnapshot(item, _transcript[index]);
    }

    private void PruneChangedCachedItems(
        IImmutableList<ConversationMessageSnapshot> oldTranscript,
        IImmutableList<ConversationMessageSnapshot> newTranscript,
        bool sameConversation)
    {
        if (!sameConversation || _matchesSnapshot is null)
        {
            _cache.Clear();
            return;
        }

        foreach (var entry in _cache.ToArray())
        {
            if (entry.Key >= newTranscript.Count
                || entry.Key >= oldTranscript.Count
                || !_matchesSnapshot(entry.Value, newTranscript[entry.Key]))
            {
                _cache.Remove(entry.Key);
            }
        }
    }

    private void RaiseReset()
    {
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged("Item[]");
    }

    private void RaiseAppend(int startIndex, int count)
    {
        if (count <= 0)
        {
            OnPropertyChanged("Item[]");
            return;
        }

        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                new ProjectedRangeList(this, startIndex, count),
                startIndex));
        OnPropertyChanged("Item[]");
    }

    private static bool CanPublishAppend(
        IImmutableList<ConversationMessageSnapshot> oldTranscript,
        IImmutableList<ConversationMessageSnapshot> newTranscript,
        bool sameConversation)
    {
        if (!sameConversation || oldTranscript.Count <= 0 || newTranscript.Count <= oldTranscript.Count)
        {
            return false;
        }

        for (var index = 0; index < oldTranscript.Count; index++)
        {
            if (!SnapshotsHaveSameStableProjection(oldTranscript[index], newTranscript[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SnapshotsHaveSameStableProjection(
        ConversationMessageSnapshot left,
        ConversationMessageSnapshot right)
        => string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && left.Timestamp == right.Timestamp
            && left.IsOutgoing == right.IsOutgoing
            && string.Equals(left.ContentType ?? string.Empty, right.ContentType ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(left.Title ?? string.Empty, right.Title ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(left.TextContent ?? string.Empty, right.TextContent ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(left.ImageData ?? string.Empty, right.ImageData ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(left.ImageMimeType ?? string.Empty, right.ImageMimeType ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(left.AudioData ?? string.Empty, right.AudioData ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(left.AudioMimeType ?? string.Empty, right.AudioMimeType ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(left.ToolCallId, right.ToolCallId, StringComparison.Ordinal)
            && left.ToolCallKind == right.ToolCallKind
            && left.ToolCallStatus == right.ToolCallStatus
            && string.Equals(left.ToolCallJson, right.ToolCallJson, StringComparison.Ordinal)
            && ToolCallContentSnapshots.SequenceEquals(left.ToolCallContent, right.ToolCallContent)
            && ToolCallContentSnapshots.LocationsSequenceEquals(left.ToolCallLocations, right.ToolCallLocations)
            && string.Equals(left.ModeId, right.ModeId, StringComparison.Ordinal)
            && PlanEntriesHaveSameStableProjection(left.PlanEntry, right.PlanEntry);

    private static bool PlanEntriesHaveSameStableProjection(
        ConversationPlanEntrySnapshot? left,
        ConversationPlanEntrySnapshot? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.Content ?? string.Empty, right.Content ?? string.Empty, StringComparison.Ordinal)
            && left.Status == right.Status
            && left.Priority == right.Priority;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class ProjectedRangeList : IList
    {
        private readonly ChatTranscriptVirtualizedMessageCollection _owner;
        private readonly int _startIndex;

        public ProjectedRangeList(ChatTranscriptVirtualizedMessageCollection owner, int startIndex, int count)
        {
            _owner = owner;
            _startIndex = startIndex;
            Count = count;
        }

        public int Count { get; }

        public bool IsFixedSize => true;

        public bool IsReadOnly => true;

        public bool IsSynchronized => false;

        public object SyncRoot => _owner;

        public object? this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _owner[_startIndex + index];
            }
            set => throw new NotSupportedException();
        }

        public int Add(object? value) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public bool Contains(object? value) => IndexOf(value) >= 0;

        public int IndexOf(object? value)
        {
            if (value is not ChatMessageViewModel message)
            {
                return -1;
            }

            var absoluteIndex = _owner.IndexOf(message);
            return absoluteIndex >= _startIndex && absoluteIndex < _startIndex + Count
                ? absoluteIndex - _startIndex
                : -1;
        }

        public void Insert(int index, object? value) => throw new NotSupportedException();

        public void Remove(object? value) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();

        public void CopyTo(Array array, int index)
        {
            for (var itemIndex = 0; itemIndex < Count; itemIndex++)
            {
                array.SetValue(this[itemIndex], index + itemIndex);
            }
        }

        public IEnumerator GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }
    }
}
