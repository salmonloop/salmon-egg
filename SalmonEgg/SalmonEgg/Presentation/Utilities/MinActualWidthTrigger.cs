using Microsoft.UI.Xaml;

namespace SalmonEgg.Presentation.Utilities;

public sealed class MinActualWidthTrigger : StateTriggerBase
{
    public static readonly DependencyProperty TargetElementProperty =
        DependencyProperty.Register(
            nameof(TargetElement),
            typeof(FrameworkElement),
            typeof(MinActualWidthTrigger),
            new PropertyMetadata(null, OnTargetElementChanged));

    public static readonly DependencyProperty MinWidthProperty =
        DependencyProperty.Register(
            nameof(MinWidth),
            typeof(double),
            typeof(MinActualWidthTrigger),
            new PropertyMetadata(0d, OnMinWidthChanged));

    private FrameworkElement? _attachedElement;

    public FrameworkElement? TargetElement
    {
        get => (FrameworkElement?)GetValue(TargetElementProperty);
        set => SetValue(TargetElementProperty, value);
    }

    public double MinWidth
    {
        get => (double)GetValue(MinWidthProperty);
        set => SetValue(MinWidthProperty, value);
    }

    private static void OnTargetElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MinActualWidthTrigger trigger)
        {
            trigger.AttachToTargetElement(e.OldValue as FrameworkElement, e.NewValue as FrameworkElement);
        }
    }

    private static void OnMinWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MinActualWidthTrigger trigger)
        {
            trigger.UpdateTriggerState();
        }
    }

    private void AttachToTargetElement(FrameworkElement? previous, FrameworkElement? current)
    {
        if (previous is not null)
        {
            previous.SizeChanged -= OnTargetElementSizeChanged;
            previous.Loaded -= OnTargetElementLoaded;
        }

        _attachedElement = current;

        if (current is not null)
        {
            current.SizeChanged += OnTargetElementSizeChanged;
            current.Loaded += OnTargetElementLoaded;
        }

        UpdateTriggerState();
    }

    private void OnTargetElementLoaded(object sender, RoutedEventArgs e)
        => UpdateTriggerState();

    private void OnTargetElementSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateTriggerState();

    private void UpdateTriggerState()
    {
        var width = _attachedElement?.ActualWidth ?? 0;
        SetActive(width >= MinWidth && width > 0);
    }
}
