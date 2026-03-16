using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace SalmonEgg.Presentation.Behaviors;

public static class FlyoutMonitor
{
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.RegisterAttached(
            "IsOpen",
            typeof(bool),
            typeof(FlyoutMonitor),
            new PropertyMetadata(false, OnIsOpenChanged));

    public static bool GetIsOpen(DependencyObject obj) => (bool)obj.GetValue(IsOpenProperty);

    public static void SetIsOpen(DependencyObject obj, bool value) => obj.SetValue(IsOpenProperty, value);

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if (FlyoutBase.GetAttachedFlyout(element) is not FlyoutBase flyout)
        {
            return;
        }

        var shouldOpen = e.NewValue is bool value && value;
        if (shouldOpen)
        {
            if (!flyout.IsOpen)
            {
                FlyoutBase.ShowAttachedFlyout(element);
            }
        }
        else
        {
            if (flyout.IsOpen)
            {
                flyout.Hide();
            }
        }
    }
}
