using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Transcript;

namespace SalmonEgg.Presentation.Transcript;

public sealed class ListViewTranscriptItemsSource :
    IList,
    INotifyCollectionChanged,
    INotifyPropertyChanged,
    IItemsRangeInfo,
    IDisposable
{
    private ChatTranscriptVirtualizedMessageCollection _source = EmptySource;
    private readonly Dictionary<int, ChatMessageViewModel> _cache = new();
    private bool _disposed;
    private int _nativeObservedCount;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Count => _source.Count;

    public bool IsFixedSize => true;

    public bool IsReadOnly => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public object? this[int index]
    {
        get => GetOrCreate(index);
        set => throw new NotSupportedException();
    }

    public void Attach(ChatTranscriptVirtualizedMessageCollection source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(_source, source))
        {
            return;
        }

        _source.CollectionChanged -= OnSourceCollectionChanged;
        _source.PropertyChanged -= OnSourcePropertyChanged;
        _source = source;
        _source.CollectionChanged += OnSourceCollectionChanged;
        _source.PropertyChanged += OnSourcePropertyChanged;
        _cache.Clear();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PublishNativeReset();
    }

    public void Detach()
    {
        if (ReferenceEquals(_source, EmptySource))
        {
            return;
        }

        _source.CollectionChanged -= OnSourceCollectionChanged;
        _source.PropertyChanged -= OnSourcePropertyChanged;
        _source = EmptySource;
        _cache.Clear();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PublishNativeReset();
    }

    private static ChatTranscriptVirtualizedMessageCollection EmptySource { get; } = new();

    public void RangesChanged(ItemIndexRange visibleRange, IReadOnlyList<ItemIndexRange> trackedItems)
    {
        if (_disposed)
        {
            return;
        }

        trackedItems ??= [];
        foreach (var index in EnumerateRange(visibleRange))
        {
            _ = GetOrCreate(index);
        }

        foreach (var range in trackedItems)
        {
            foreach (var index in EnumerateRange(range))
            {
                _ = GetOrCreate(index);
            }
        }

        PruneCache(visibleRange, trackedItems);
    }

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Contains(object? value) => value is ChatMessageViewModel message && IndexOf(message) >= 0;

    public int IndexOf(object? value)
    {
        if (value is not ChatMessageViewModel message)
        {
            return -1;
        }

        foreach (var entry in _cache)
        {
            if (ReferenceEquals(entry.Value, message))
            {
                return entry.Key;
            }
        }

        return _source.IndexOf(message);
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Detach();
    }

    private ChatMessageViewModel GetOrCreate(int index)
    {
        if (index < 0 || index >= _source.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (_cache.TryGetValue(index, out var message)
            && _source.MatchesItem(index, message))
        {
            return message;
        }

        var projected = _source.CreateItem(index);
        _cache[index] = projected;
        return projected;
    }

    private IEnumerable<int> EnumerateRange(ItemIndexRange range)
    {
        if (_source.Count == 0 || range.Length <= 0)
        {
            yield break;
        }

        var first = Math.Clamp(range.FirstIndex, 0, _source.Count - 1);
        var last = Math.Clamp((int)range.LastIndex, first, _source.Count - 1);
        for (var index = first; index <= last; index++)
        {
            yield return index;
        }
    }

    private void PruneCache(ItemIndexRange visibleRange, IReadOnlyList<ItemIndexRange> trackedItems)
    {
        if (_cache.Count == 0)
        {
            return;
        }

        foreach (var index in _cache.Keys.ToArray())
        {
            if (index < 0
                || index >= _source.Count
                || !ShouldKeepCachedIndex(index, visibleRange, trackedItems))
            {
                _cache.Remove(index);
            }
        }
    }

    private static bool ShouldKeepCachedIndex(
        int index,
        ItemIndexRange visibleRange,
        IReadOnlyList<ItemIndexRange> trackedItems)
    {
        if (ContainsIndex(visibleRange, index))
        {
            return true;
        }

        foreach (var range in trackedItems)
        {
            if (ContainsIndex(range, index))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsIndex(ItemIndexRange range, int index)
        => range.Length > 0
            && index >= range.FirstIndex
            && index <= range.LastIndex;

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                // A native Reset emitted for an earlier Replace already covers the
                // source's current count; replaying the matching Add would be invalid.
                if (_source.Count <= _nativeObservedCount)
                {
                    return;
                }

                CollectionChanged?.Invoke(
                    this,
                    new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Add,
                        new ProjectedRangeList(this, e.NewStartingIndex, e.NewItems?.Count ?? 0),
                        e.NewStartingIndex));
                _nativeObservedCount = _source.Count;
                return;

            case NotifyCollectionChangedAction.Replace:
                InvalidateItem(e.NewStartingIndex);
                return;

            case NotifyCollectionChangedAction.Reset:
                _cache.Clear();
                PublishNativeReset();
                return;

            default:
                RemoveInvalidCachedItems();
                CollectionChanged?.Invoke(this, e);
                _nativeObservedCount = _source.Count;
                return;
        }
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        PropertyChanged?.Invoke(this, e);
    }

    private void RemoveInvalidCachedItems()
    {
        foreach (var entry in _cache.ToArray())
        {
            if (entry.Key >= _source.Count
                || !_source.MatchesItem(entry.Key, entry.Value))
            {
                _cache.Remove(entry.Key);
            }
        }
    }

    private void InvalidateItem(int index)
    {
        if (index < 0 || index >= _source.Count)
        {
            RemoveInvalidCachedItems();
            return;
        }

        if (!_cache.Remove(index))
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PublishNativeReset();
    }

    private void PublishNativeReset()
    {
        _nativeObservedCount = _source.Count;
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private sealed class ProjectedRangeList : IList
    {
        private readonly ListViewTranscriptItemsSource _owner;
        private readonly int _startIndex;

        public ProjectedRangeList(ListViewTranscriptItemsSource owner, int startIndex, int count)
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
