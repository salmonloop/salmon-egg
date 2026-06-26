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

    public static ChatTranscriptVirtualizedMessageCollection Create(
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript,
        Func<ConversationMessageSnapshot, int, ChatMessageViewModel> projector,
        Func<ChatMessageViewModel, ConversationMessageSnapshot, bool> matchesSnapshot)
    {
        var collection = new ChatTranscriptVirtualizedMessageCollection();
        collection.ReplaceWithoutNotifications(conversationId, transcript, projector, matchesSnapshot);
        return collection;
    }

    public bool CanApplyInPlace(
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript)
    {
        var messages = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var sameConversation = string.Equals(_conversationId, conversationId, StringComparison.Ordinal);
        return sameConversation
            && messages.Count >= _transcript.Count;
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
        var addedCount = Math.Max(0, messages.Count - oldCount);
        var isAppend = sameConversation && messages.Count > oldCount;

        _conversationId = conversationId;
        _transcript = messages;
        _projector = projector;
        _matchesSnapshot = matchesSnapshot;

        if (unchangedTranscript)
        {
            return;
        }

        if (isAppend)
        {
            PublishChangedCachedItems(oldTranscript, messages, sameConversation);
        }
        else
        {
            PublishChangedItems(oldTranscript, messages, sameConversation);
        }

        if (addedCount > 0)
        {
            RaiseAppend(oldCount, addedCount);
        }

        if (oldCount != messages.Count)
        {
            OnPropertyChanged(nameof(Count));
        }
    }

    private void ReplaceWithoutNotifications(
        string? conversationId,
        IImmutableList<ConversationMessageSnapshot> transcript,
        Func<ConversationMessageSnapshot, int, ChatMessageViewModel> projector,
        Func<ChatMessageViewModel, ConversationMessageSnapshot, bool> matchesSnapshot)
    {
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(matchesSnapshot);

        _conversationId = conversationId;
        _transcript = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        _projector = projector;
        _matchesSnapshot = matchesSnapshot;
        _cache.Clear();
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

    public int IndexOfProjectionItemKey(string projectionItemKey)
    {
        if (string.IsNullOrWhiteSpace(projectionItemKey))
        {
            return -1;
        }

        for (var index = 0; index < _transcript.Count; index++)
        {
            if (string.Equals(
                    projectionItemKey,
                    TranscriptProjectionRestoreTokenProjector.CreateProjectionItemKey(_transcript[index], index),
                    StringComparison.Ordinal))
            {
                return index;
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

    private void PublishChangedCachedItems(
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
            if (entry.Key >= newTranscript.Count || entry.Key >= oldTranscript.Count)
            {
                _cache.Remove(entry.Key);
                continue;
            }

            if (!_matchesSnapshot(entry.Value, newTranscript[entry.Key]))
            {
                var oldItem = entry.Value;
                var newItem = CreateItem(entry.Key);
                _cache[entry.Key] = newItem;
                RaiseReplace(entry.Key, oldItem, newItem);
            }
        }
    }

    private void PublishChangedItems(
        IImmutableList<ConversationMessageSnapshot> oldTranscript,
        IImmutableList<ConversationMessageSnapshot> newTranscript,
        bool sameConversation)
    {
        if (!sameConversation || _matchesSnapshot is null || _projector is null)
        {
            _cache.Clear();
            return;
        }

        var sharedCount = Math.Min(oldTranscript.Count, newTranscript.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            if (_cache.TryGetValue(index, out var cachedItem))
            {
                if (!_matchesSnapshot(cachedItem, newTranscript[index]))
                {
                    var newItem = CreateItem(index);
                    _cache[index] = newItem;
                    RaiseReplace(index, cachedItem, newItem);
                }

                continue;
            }

            if (!SnapshotProjectionEquals(oldTranscript[index], newTranscript[index]))
            {
                RaiseReplace(
                    index,
                    _projector(oldTranscript[index], index),
                    _projector(newTranscript[index], index));
            }
        }

        foreach (var entry in _cache.ToArray())
        {
            if (entry.Key >= newTranscript.Count)
            {
                _cache.Remove(entry.Key);
            }
        }
    }

    private void RaiseReplace(
        int index,
        ChatMessageViewModel oldItem,
        ChatMessageViewModel newItem)
    {
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace,
                newItem,
                oldItem,
                index));
    }

    private static bool SnapshotProjectionEquals(
        ConversationMessageSnapshot oldSnapshot,
        ConversationMessageSnapshot newSnapshot)
        => ReferenceEquals(oldSnapshot, newSnapshot)
           || (string.Equals(oldSnapshot.Id, newSnapshot.Id, StringComparison.Ordinal)
               && oldSnapshot.Timestamp == newSnapshot.Timestamp
               && oldSnapshot.IsOutgoing == newSnapshot.IsOutgoing
               && string.Equals(oldSnapshot.ContentType ?? string.Empty, newSnapshot.ContentType ?? string.Empty, StringComparison.Ordinal)
               && string.Equals(oldSnapshot.Title ?? string.Empty, newSnapshot.Title ?? string.Empty, StringComparison.Ordinal)
               && string.Equals(oldSnapshot.TextContent ?? string.Empty, newSnapshot.TextContent ?? string.Empty, StringComparison.Ordinal)
               && string.Equals(oldSnapshot.ImageData ?? string.Empty, newSnapshot.ImageData ?? string.Empty, StringComparison.Ordinal)
               && string.Equals(oldSnapshot.ImageMimeType ?? string.Empty, newSnapshot.ImageMimeType ?? string.Empty, StringComparison.Ordinal)
               && string.Equals(oldSnapshot.AudioData ?? string.Empty, newSnapshot.AudioData ?? string.Empty, StringComparison.Ordinal)
               && string.Equals(oldSnapshot.AudioMimeType ?? string.Empty, newSnapshot.AudioMimeType ?? string.Empty, StringComparison.Ordinal)
               && string.Equals(oldSnapshot.ToolCallId, newSnapshot.ToolCallId, StringComparison.Ordinal)
               && oldSnapshot.ToolCallKind == newSnapshot.ToolCallKind
               && oldSnapshot.ToolCallStatus == newSnapshot.ToolCallStatus
               && string.Equals(oldSnapshot.ToolCallJson, newSnapshot.ToolCallJson, StringComparison.Ordinal)
               && string.Equals(oldSnapshot.ToolCallRawInputJson, newSnapshot.ToolCallRawInputJson, StringComparison.Ordinal)
               && string.Equals(oldSnapshot.ToolCallRawOutputJson, newSnapshot.ToolCallRawOutputJson, StringComparison.Ordinal)
               && ToolCallContentSnapshots.SequenceEquals(oldSnapshot.ToolCallContent, newSnapshot.ToolCallContent)
               && ToolCallContentSnapshots.LocationsSequenceEquals(oldSnapshot.ToolCallLocations, newSnapshot.ToolCallLocations)
               && string.Equals(oldSnapshot.ModeId, newSnapshot.ModeId, StringComparison.Ordinal)
               && PlanEntrySnapshotEquals(oldSnapshot.PlanEntry, newSnapshot.PlanEntry));

    private static bool PlanEntrySnapshotEquals(
        ConversationPlanEntrySnapshot? oldSnapshot,
        ConversationPlanEntrySnapshot? newSnapshot)
    {
        if (oldSnapshot is null && newSnapshot is null)
        {
            return true;
        }

        if (oldSnapshot is null || newSnapshot is null)
        {
            return false;
        }

        return string.Equals(oldSnapshot.Content ?? string.Empty, newSnapshot.Content ?? string.Empty, StringComparison.Ordinal)
            && oldSnapshot.Status == newSnapshot.Status
            && oldSnapshot.Priority == newSnapshot.Priority;
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
