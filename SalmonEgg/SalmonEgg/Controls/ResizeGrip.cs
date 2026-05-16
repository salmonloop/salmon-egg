using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace SalmonEgg.Controls;

public sealed partial class ResizeGrip : Control
{
    public ResizeGrip()
    {
        ApplyPlatformCursor();
    }

    partial void ApplyPlatformCursor();

    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        VisualStateManager.GoToState(this, "PointerOver", true);
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);
        VisualStateManager.GoToState(this, "Normal", true);
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        VisualStateManager.GoToState(this, "Pressed", true);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        VisualStateManager.GoToState(this, "PointerOver", true);
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        VisualStateManager.GoToState(this, "Normal", true);
    }
}
