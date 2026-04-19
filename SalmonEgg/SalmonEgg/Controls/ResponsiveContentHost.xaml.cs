using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models;

namespace SalmonEgg.Controls;

public sealed partial class ResponsiveContentHost : UserControl
{
    private bool _isUpdatingColumns;
    private bool? _isWide;

    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register(
            nameof(Child),
            typeof(object),
            typeof(ResponsiveContentHost),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MaxContentWidthProperty =
        DependencyProperty.Register(
            nameof(MaxContentWidth),
            typeof(double),
            typeof(ResponsiveContentHost),
            new PropertyMetadata(UiLayout.ContentMaxWidth, OnLayoutPropertyChanged));

    public static readonly DependencyProperty MinGutterProperty =
        DependencyProperty.Register(
            nameof(MinGutter),
            typeof(double),
            typeof(ResponsiveContentHost),
            new PropertyMetadata(24d, OnLayoutPropertyChanged));

    public object? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    public double MaxContentWidth
    {
        get => (double)GetValue(MaxContentWidthProperty);
        set => SetValue(MaxContentWidthProperty, value);
    }

    public double MinGutter
    {
        get => (double)GetValue(MinGutterProperty);
        set => SetValue(MinGutterProperty, value);
    }

    public ResponsiveContentHost()
    {
        InitializeComponent();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResponsiveContentHost host)
        {
            host._isWide = null; // Force layout recalculation
            host.UpdateColumns(host.LayoutRoot?.ActualWidth ?? host.ActualWidth);
        }
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateColumns(e.NewSize.Width);
    }

    private void UpdateColumns(double availableWidth)
    {
        if (_isUpdatingColumns || availableWidth <= 0)
        {
            return;
        }

        var max = MaxContentWidth;
        var minGutter = Math.Max(0, MinGutter);

        if (max <= 0)
        {
            SetNarrow(minGutter);
            return;
        }

        var wideThreshold = max + (minGutter * 2);
        bool shouldBeWide = availableWidth >= wideThreshold;

        if (_isWide != shouldBeWide)
        {
            if (shouldBeWide)
            {
                SetWide(max);
            }
            else
            {
                SetNarrow(minGutter);
            }
        }
    }

    private void SetWide(double max)
    {
        _isUpdatingColumns = true;
        try
        {
            _isWide = true;
            ContentColumn.Width = new GridLength(max, GridUnitType.Pixel);
            LeftGutter.Width = new GridLength(1, GridUnitType.Star);
            RightGutter.Width = new GridLength(1, GridUnitType.Star);
        }
        finally
        {
            _isUpdatingColumns = false;
        }
    }

    private void SetNarrow(double minGutter)
    {
        _isUpdatingColumns = true;
        try
        {
            _isWide = false;
            ContentColumn.Width = new GridLength(1, GridUnitType.Star);
            LeftGutter.Width = new GridLength(minGutter, GridUnitType.Pixel);
            RightGutter.Width = new GridLength(minGutter, GridUnitType.Pixel);
        }
        finally
        {
            _isUpdatingColumns = false;
        }
    }
}
