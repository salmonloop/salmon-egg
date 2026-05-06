using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;

namespace SalmonEgg.Presentation.Behaviors;

public static class ScrollViewerViewportMonitor
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ScrollViewerViewportMonitor),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ViewportChangeTokenProperty =
        DependencyProperty.RegisterAttached(
            "ViewportChangeToken",
            typeof(int),
            typeof(ScrollViewerViewportMonitor),
            new PropertyMetadata(0));

    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "VerticalOffset",
            typeof(double),
            typeof(ScrollViewerViewportMonitor),
            new PropertyMetadata(-1d));

    public static readonly DependencyProperty ScrollableHeightProperty =
        DependencyProperty.RegisterAttached(
            "ScrollableHeight",
            typeof(double),
            typeof(ScrollViewerViewportMonitor),
            new PropertyMetadata(-1d));

    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsHooked",
            typeof(bool),
            typeof(ScrollViewerViewportMonitor),
            new PropertyMetadata(false));

    private static readonly DependencyProperty AttachedScrollViewerProperty =
        DependencyProperty.RegisterAttached(
            "AttachedScrollViewer",
            typeof(ScrollViewer),
            typeof(ScrollViewerViewportMonitor),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static int GetViewportChangeToken(DependencyObject obj) => (int)obj.GetValue(ViewportChangeTokenProperty);

    public static double GetVerticalOffset(DependencyObject obj) => (double)obj.GetValue(VerticalOffsetProperty);

    public static double GetScrollableHeight(DependencyObject obj) => (double)obj.GetValue(ScrollableHeightProperty);

    private static bool GetIsHooked(DependencyObject obj) => (bool)obj.GetValue(IsHookedProperty);

    private static void SetIsHooked(DependencyObject obj, bool value) => obj.SetValue(IsHookedProperty, value);

    public static ScrollViewer? GetAttachedScrollViewer(DependencyObject obj) => (ScrollViewer?)obj.GetValue(AttachedScrollViewerProperty);

    private static void SetAttachedScrollViewer(DependencyObject obj, ScrollViewer? value) => obj.SetValue(AttachedScrollViewerProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView listView)
        {
            return;
        }

        var isEnabled = e.NewValue is true;
        if (!isEnabled)
        {
            Detach(listView);
            return;
        }

        if (GetIsHooked(listView))
        {
            return;
        }

        listView.Loaded += OnListViewLoaded;
        listView.Unloaded += OnListViewUnloaded;
        listView.LayoutUpdated += OnListViewLayoutUpdated;
        SetIsHooked(listView, true);

        if (listView.IsLoaded)
        {
            AttachScrollViewer(listView);
        }
    }

    private static void OnListViewLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListView listView)
        {
            if (GetAttachedScrollViewer(listView) is null)
            {
                AttachScrollViewer(listView);
            }

            UpdateViewportState(listView);
        }
    }

    private static void OnListViewUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListView listView)
        {
            DetachScrollViewer(listView);
        }
    }

    private static void OnListViewLayoutUpdated(object? sender, object e)
    {
        if (sender is not ListView listView)
        {
            return;
        }

        if (GetAttachedScrollViewer(listView) is null)
        {
            AttachScrollViewer(listView);
        }

        UpdateViewportState(listView);
    }

    private static void AttachScrollViewer(ListView listView)
    {
        if (GetAttachedScrollViewer(listView) is not null)
        {
            UpdateViewportState(listView);
            return;
        }

        var scrollViewer = FindScrollViewer(listView);
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ViewChanged += OnScrollViewerViewChanged;
        scrollViewer.SizeChanged += OnScrollViewerSizeChanged;
        SetAttachedScrollViewer(listView, scrollViewer);
        UpdateViewportState(listView);
    }

    private static void Detach(ListView listView)
    {
        DetachScrollViewer(listView);
        if (GetIsHooked(listView))
        {
            listView.Loaded -= OnListViewLoaded;
            listView.Unloaded -= OnListViewUnloaded;
            listView.LayoutUpdated -= OnListViewLayoutUpdated;
            SetIsHooked(listView, false);
        }
    }

    private static void DetachScrollViewer(ListView listView)
    {
        if (GetAttachedScrollViewer(listView) is ScrollViewer scrollViewer)
        {
            scrollViewer.ViewChanged -= OnScrollViewerViewChanged;
            scrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
            SetAttachedScrollViewer(listView, null);
        }
    }

    private static void OnScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (FindOwningListView(scrollViewer) is ListView listView)
        {
            UpdateViewportState(listView);
        }
    }

    private static void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (FindOwningListView(scrollViewer) is ListView listView)
        {
            UpdateViewportState(listView);
        }
    }

    private static void UpdateViewportState(ListView listView)
    {
        if (GetAttachedScrollViewer(listView) is not ScrollViewer scrollViewer)
        {
            return;
        }

        var verticalOffset = scrollViewer.VerticalOffset;
        var scrollableHeight = scrollViewer.ScrollableHeight;
        var previousVerticalOffset = GetVerticalOffset(listView);
        var previousScrollableHeight = GetScrollableHeight(listView);

        if (AreClose(previousVerticalOffset, verticalOffset)
            && AreClose(previousScrollableHeight, scrollableHeight))
        {
            return;
        }

        listView.SetValue(VerticalOffsetProperty, verticalOffset);
        listView.SetValue(ScrollableHeightProperty, scrollableHeight);
        listView.SetValue(ViewportChangeTokenProperty, GetViewportChangeToken(listView) + 1);
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= 0.5d;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer scrollViewer)
        {
            return scrollViewer;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var result = FindScrollViewer(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static ListView? FindOwningListView(DependencyObject child)
    {
        DependencyObject? current = child;
        while (current is not null)
        {
            if (current is ListView listView)
            {
                return listView;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
