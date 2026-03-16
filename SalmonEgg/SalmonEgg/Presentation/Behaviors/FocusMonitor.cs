using Microsoft.UI.Xaml;

namespace SalmonEgg.Presentation.Behaviors;

public static class FocusMonitor
{
    public static readonly DependencyProperty IsFocusedProperty =
        DependencyProperty.RegisterAttached(
            "IsFocused",
            typeof(bool),
            typeof(FocusMonitor),
            new PropertyMetadata(false, OnIsFocusedChanged));

    public static bool GetIsFocused(DependencyObject obj) => (bool)obj.GetValue(IsFocusedProperty);

    public static void SetIsFocused(DependencyObject obj, bool value) => obj.SetValue(IsFocusedProperty, value);

    private static void OnIsFocusedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.GotFocus -= OnElementGotFocus;
        element.LostFocus -= OnElementLostFocus;
        element.GotFocus += OnElementGotFocus;
        element.LostFocus += OnElementLostFocus;
    }

    private static void OnElementGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject element)
        {
            SetIsFocused(element, true);
        }
    }

    private static void OnElementLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject element)
        {
            SetIsFocused(element, false);
        }
    }
}
