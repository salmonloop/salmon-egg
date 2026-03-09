using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SalmonEgg.Controls;

public sealed partial class ResponsiveSettingsHost : UserControl
{
    private bool _isUpdatingColumns;
    private bool _isWide;

    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register(
            nameof(Child),
            typeof(object),
            typeof(ResponsiveSettingsHost),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MaxContentWidthProperty =
        DependencyProperty.Register(
            nameof(MaxContentWidth),
            typeof(double),
            typeof(ResponsiveSettingsHost),
            new PropertyMetadata(780d, OnMaxContentWidthChanged));

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

    public ResponsiveSettingsHost()
    {
        InitializeComponent();
    }

    private static void OnMaxContentWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResponsiveSettingsHost host)
        {
            host.UpdateColumns(host.LayoutRoot?.ActualWidth ?? host.ActualWidth);
        }
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateColumns(e.NewSize.Width);
    }

    private void UpdateColumns(double availableWidth)
    {
        if (_isUpdatingColumns)
        {
            return;
        }

        if (availableWidth <= 0)
        {
            return;
        }

        var max = MaxContentWidth;
        if (max <= 0)
        {
            SetNarrow();
            return;
        }

        if (availableWidth > max && !_isWide)
        {
            SetWide(max);
        }
        else if (availableWidth <= max && _isWide)
        {
            SetNarrow();
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

    private void SetNarrow()
    {
        _isUpdatingColumns = true;
        try
        {
            _isWide = false;
            ContentColumn.Width = new GridLength(1, GridUnitType.Star);
            LeftGutter.Width = new GridLength(0);
            RightGutter.Width = new GridLength(0);
        }
        finally
        {
            _isUpdatingColumns = false;
        }
    }
}
