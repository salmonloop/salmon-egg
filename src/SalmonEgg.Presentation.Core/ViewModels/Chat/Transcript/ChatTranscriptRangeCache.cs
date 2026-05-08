using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Chat.Transcript;

public sealed class ChatTranscriptRangeCache
{
    private readonly Dictionary<int, ChatMessageViewModel> _cache = new();
    private ChatTranscriptVirtualizedMessageCollection _source;

    public ChatTranscriptRangeCache(ChatTranscriptVirtualizedMessageCollection source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public int Count => _source.Count;

    public ChatMessageViewModel this[int index]
    {
        get
        {
            if (_cache.TryGetValue(index, out var message)
                && _source.MatchesItem(index, message))
            {
                return message;
            }

            var projected = _source.CreateItem(index);
            _cache[index] = projected;
            return projected;
        }
    }

    public void ResetSource(ChatTranscriptVirtualizedMessageCollection source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _cache.Clear();
    }

    public void Clear()
    {
        _cache.Clear();
    }

    public void PruneChangedItems()
    {
        foreach (var entry in _cache.ToArray())
        {
            if (!_source.MatchesItem(entry.Key, entry.Value))
            {
                _cache.Remove(entry.Key);
            }
        }
    }

    public void ApplyRequiredRanges(
        TranscriptVirtualizationRange visibleRange,
        IReadOnlyList<TranscriptVirtualizationRange> trackedRanges)
    {
        var ranges = BuildValidRanges(visibleRange, trackedRanges);
        foreach (var range in ranges)
        {
            for (var index = range.FirstIndex; index <= range.LastIndex; index++)
            {
                _ = this[index];
            }
        }

        PruneCacheOutside(ranges);
    }

    public bool IsItemCached(int index) => _cache.ContainsKey(index);

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

        return _source.IndexOf(item);
    }

    public void CopyTo(Array array, int index)
    {
        for (var itemIndex = 0; itemIndex < _source.Count; itemIndex++)
        {
            array.SetValue(this[itemIndex], index + itemIndex);
        }
    }

    private List<TranscriptVirtualizationRange> BuildValidRanges(
        TranscriptVirtualizationRange visibleRange,
        IReadOnlyList<TranscriptVirtualizationRange> trackedRanges)
    {
        var ranges = new List<TranscriptVirtualizationRange>();
        AddIfValid(visibleRange);
        if (trackedRanges is not null)
        {
            foreach (var range in trackedRanges)
            {
                AddIfValid(range);
            }
        }

        return ranges;

        void AddIfValid(TranscriptVirtualizationRange range)
        {
            if (range.Length <= 0 || _source.Count <= 0)
            {
                return;
            }

            var first = Math.Clamp(range.FirstIndex, 0, _source.Count - 1);
            var last = Math.Clamp(range.LastIndex, 0, _source.Count - 1);
            if (last < first)
            {
                return;
            }

            ranges.Add(new TranscriptVirtualizationRange(first, last - first + 1));
        }
    }

    private void PruneCacheOutside(IReadOnlyList<TranscriptVirtualizationRange> ranges)
    {
        foreach (var index in _cache.Keys.ToArray())
        {
            if (!ranges.Any(range => range.Contains(index)))
            {
                _cache.Remove(index);
            }
        }
    }
}
