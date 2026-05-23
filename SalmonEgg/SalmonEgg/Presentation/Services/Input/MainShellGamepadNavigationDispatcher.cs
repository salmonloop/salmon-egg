using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Services.Navigation;
using XamlFocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class MainShellGamepadNavigationDispatcher : IGamepadNavigationDispatcher
{
    private readonly IShellBackNavigationService _shellBackNavigation;

    public MainShellGamepadNavigationDispatcher(IShellBackNavigationService shellBackNavigation)
    {
        _shellBackNavigation = shellBackNavigation ?? throw new ArgumentNullException(nameof(shellBackNavigation));
    }

    public bool TryDispatch(GamepadNavigationIntent intent)
    {
        if (TryConsumeNavigationIntent(intent))
        {
            return true;
        }

        return intent == GamepadNavigationIntent.Back
            && _shellBackNavigation.TryGoBack();
    }

    private static bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        var current = GetFocusedElement();
        while (current != null)
        {
            if (current is INavigationIntentConsumer consumer
                && consumer.TryConsumeNavigationIntent(intent))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static DependencyObject? GetFocusedElement()
    {
        var root = GetRootElement();
        if (root?.XamlRoot is null)
        {
            return null;
        }

        return XamlFocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
    }

    private static FrameworkElement? GetRootElement()
    {
        return App.MainWindowInstance?.Content as FrameworkElement;
    }

}
