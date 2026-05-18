using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models;

namespace SalmonEgg.Controls;

public sealed partial class ResponsiveContentHost : UserControl
{
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
            new PropertyMetadata(UiLayout.ContentMaxWidth));

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

    public ResponsiveContentHost()
    {
        InitializeComponent();
    }
}
