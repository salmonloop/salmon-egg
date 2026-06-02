using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.ViewModels.Chat.Transcript;
using Windows.Foundation;

namespace SalmonEgg.Presentation.Transcript;

public sealed class ListViewTranscriptViewportHost : ITranscriptViewportHost
{
    private readonly ListViewBase _listView;
    private readonly Func<TranscriptVirtualizationRange?>? _visibleRangeProvider;
    private ScrollViewer? _scrollViewer;
    private bool _viewportChangedQueued;

    public ListViewTranscriptViewportHost(
        ListViewBase listView,
        Func<TranscriptVirtualizationRange?>? visibleRangeProvider = null)
    {
        _listView = listView ?? throw new ArgumentNullException(nameof(listView));
        _visibleRangeProvider = visibleRangeProvider;
        _listView.ContainerContentChanging += OnContainerContentChanging;
        _listView.SizeChanged += OnSizeChanged;
        _listView.Loaded += OnListViewLoaded;
        _listView.Unloaded += OnListViewUnloaded;
        AttachScrollViewer();
    }

    public event EventHandler? ViewportChanged;

    public bool HasRealizedItem(int index) => index >= 0 && _listView.ContainerFromIndex(index) is not null;

    public bool TryGetFirstVisibleIndex(int itemCount, out int index)
    {
        index = -1;
        if (itemCount <= 0)
        {
            return false;
        }

        var range = _visibleRangeProvider?.Invoke() is { Length: > 0 } visibleRange
            ? ClampRange(visibleRange, itemCount)
            : new TranscriptVirtualizationRange(0, itemCount);

        return TryGetFirstVisibleIndexInRange(range, out index);
    }

    private bool TryGetFirstVisibleIndexInRange(TranscriptVirtualizationRange range, out int index)
    {
        index = -1;
        var viewportTop = _listView.Padding.Top;
        var viewportBottom = _listView.ActualHeight - _listView.Padding.Bottom;
        for (var candidate = range.FirstIndex; candidate <= range.LastIndex; candidate++)
        {
            if (!TryGetContainerAnchor(candidate, out var anchor))
            {
                continue;
            }

            var relativeOrigin = anchor.TransformToVisual(_listView).TransformPoint(default);
            var itemTop = relativeOrigin.Y;
            var itemBottom = itemTop + anchor.ActualHeight;
            if (itemBottom <= viewportTop || itemTop >= viewportBottom)
            {
                continue;
            }

            index = candidate;
            return true;
        }

        return false;
    }

    private bool TryGetVisibleIndexBounds(int itemCount, out int firstVisibleIndex, out int lastVisibleIndex)
    {
        firstVisibleIndex = -1;
        lastVisibleIndex = -1;
        if (itemCount <= 0)
        {
            return false;
        }

        var range = _visibleRangeProvider?.Invoke() is { Length: > 0 } visibleRange
            ? ClampRange(visibleRange, itemCount)
            : new TranscriptVirtualizationRange(0, itemCount);
        var viewportTop = _listView.Padding.Top;
        var viewportBottom = _listView.ActualHeight - _listView.Padding.Bottom;
        for (var candidate = range.FirstIndex; candidate <= range.LastIndex; candidate++)
        {
            if (!TryGetContainerAnchor(candidate, out var anchor))
            {
                continue;
            }

            var relativeOrigin = anchor.TransformToVisual(_listView).TransformPoint(default);
            var itemTop = relativeOrigin.Y;
            var itemBottom = itemTop + anchor.ActualHeight;
            if (itemBottom <= viewportTop || itemTop >= viewportBottom)
            {
                continue;
            }

            firstVisibleIndex = firstVisibleIndex < 0
                ? candidate
                : firstVisibleIndex;
            lastVisibleIndex = candidate;
        }

        return firstVisibleIndex >= 0 && lastVisibleIndex >= firstVisibleIndex;
    }

    public void ScrollItemIntoView(
        int index,
        TranscriptItemScrollAlignment alignment = TranscriptItemScrollAlignment.Default)
    {
        if (index < 0 || index >= _listView.Items.Count)
        {
            return;
        }

        var item = _listView.Items[index];
        if (item is null)
        {
            return;
        }

        _listView.ScrollIntoView(item, ToNativeAlignment(alignment));
    }

    public bool TryFocusItem(int index, FocusState focusState)
    {
        if (index < 0 || index >= _listView.Items.Count)
        {
            return false;
        }

        if (_listView.ContainerFromIndex(index) is not ListViewItem container)
        {
            return false;
        }

        return container.Focus(focusState)
            || container.Focus(FocusState.Programmatic);
    }

    public bool TryScrollByItems(int itemDelta)
    {
        if (itemDelta == 0 || _listView.Items.Count <= 0)
        {
            return false;
        }

        if (!TryGetFirstVisibleIndex(_listView.Items.Count, out var firstVisibleIndex))
        {
            return false;
        }

        var targetIndex = Math.Clamp(firstVisibleIndex + itemDelta, 0, _listView.Items.Count - 1);
        if (targetIndex == firstVisibleIndex)
        {
            return false;
        }

        ScrollItemIntoView(targetIndex, TranscriptItemScrollAlignment.Leading);
        return true;
    }

    public bool TryScrollByPages(int pageDelta)
    {
        if (pageDelta == 0 || _listView.Items.Count <= 0)
        {
            return false;
        }

        if (!TryGetVisibleIndexBounds(_listView.Items.Count, out var firstVisibleIndex, out var lastVisibleIndex))
        {
            return false;
        }

        var visibleCount = Math.Max(1, (lastVisibleIndex - firstVisibleIndex) + 1);
        var targetIndex = pageDelta > 0
            ? Math.Clamp(firstVisibleIndex + visibleCount, 0, _listView.Items.Count - 1)
            : Math.Clamp(firstVisibleIndex - visibleCount, 0, _listView.Items.Count - 1);
        if (targetIndex == firstVisibleIndex)
        {
            return false;
        }

        ScrollItemIntoView(targetIndex, TranscriptItemScrollAlignment.Leading);
        return true;
    }

    public bool TryFocusViewport(FocusState focusState)
    {
        AttachScrollViewer();
        if (_scrollViewer?.Focus(focusState) == true)
        {
            return true;
        }

        return _listView.Focus(focusState);
    }

    public bool IsAtBottom(int itemCount, double bottomThreshold, double bottomGeometryTolerance)
    {
        if (itemCount <= 0)
        {
            return true;
        }

        return IsLastItemVisiblyAtBottom(itemCount, bottomThreshold, bottomGeometryTolerance);
    }

    public bool IsLastItemVisiblyAtBottom(int itemCount, double bottomThreshold, double bottomGeometryTolerance)
    {
        if (itemCount <= 0 || !TryGetContainerAnchor(itemCount - 1, out var anchor))
        {
            return false;
        }

        Point relativeOrigin = anchor.TransformToVisual(_listView).TransformPoint(default);
        var itemTop = relativeOrigin.Y;
        var itemBottom = itemTop + anchor.ActualHeight;
        var viewportTop = _listView.Padding.Top;
        var viewportBottom = _listView.ActualHeight - bottomThreshold - _listView.Padding.Bottom;
        if (itemBottom <= viewportBottom + bottomGeometryTolerance)
        {
            return true;
        }

        var availableViewportHeight = viewportBottom - viewportTop;
        return anchor.ActualHeight <= availableViewportHeight + bottomGeometryTolerance
            && itemTop >= viewportTop - bottomGeometryTolerance
            && itemTop < viewportBottom;
    }

    public void Dispose()
    {
        DetachScrollViewer();
        _listView.Loaded -= OnListViewLoaded;
        _listView.Unloaded -= OnListViewUnloaded;
        _listView.ContainerContentChanging -= OnContainerContentChanging;
        _listView.SizeChanged -= OnSizeChanged;
        _viewportChangedQueued = false;
    }

    private bool TryGetContainerAnchor(int index, out FrameworkElement anchor)
    {
        anchor = null!;
        if (_listView.ContainerFromIndex(index) is not ListViewItem container)
        {
            return false;
        }

        anchor = container.ContentTemplateRoot as FrameworkElement ?? container;
        return true;
    }

    private static TranscriptVirtualizationRange ClampRange(TranscriptVirtualizationRange range, int itemCount)
    {
        if (itemCount <= 0 || range.Length <= 0)
        {
            return new TranscriptVirtualizationRange(0, 0);
        }

        var first = Math.Clamp(range.FirstIndex, 0, itemCount - 1);
        var last = Math.Clamp(range.LastIndex, first, itemCount - 1);
        return new TranscriptVirtualizationRange(first, last - first + 1);
    }

    private static ScrollIntoViewAlignment ToNativeAlignment(TranscriptItemScrollAlignment alignment)
        => alignment == TranscriptItemScrollAlignment.Leading
            ? ScrollIntoViewAlignment.Leading
            : ScrollIntoViewAlignment.Default;

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        QueueViewportChanged();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        QueueViewportChanged();
    }

    private void OnListViewLoaded(object sender, RoutedEventArgs e)
    {
        AttachScrollViewer();
        QueueViewportChanged();
    }

    private void OnListViewUnloaded(object sender, RoutedEventArgs e)
    {
        DetachScrollViewer();
    }

    private void OnScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        QueueViewportChanged();
    }

    private void AttachScrollViewer()
    {
        if (_scrollViewer is not null)
        {
            return;
        }

        _scrollViewer = FindDescendant<ScrollViewer>(_listView);
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged += OnScrollViewerViewChanged;
        }
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer is null)
        {
            return;
        }

        _scrollViewer.ViewChanged -= OnScrollViewerViewChanged;
        _scrollViewer = null;
    }

    private static T? FindDescendant<T>(DependencyObject? root)
        where T : DependencyObject
    {
        if (root is null)
        {
            return default;
        }

        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T match)
            {
                return match;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var index = 0; index < childCount; index++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, index));
            }
        }

        return default;
    }

    private void QueueViewportChanged()
    {
        if (_viewportChangedQueued)
        {
            return;
        }

        _viewportChangedQueued = true;
        if (!_listView.DispatcherQueue.TryEnqueue(() =>
            {
                _viewportChangedQueued = false;
                ViewportChanged?.Invoke(this, EventArgs.Empty);
            }))
        {
            _viewportChangedQueued = false;
        }
    }
}
