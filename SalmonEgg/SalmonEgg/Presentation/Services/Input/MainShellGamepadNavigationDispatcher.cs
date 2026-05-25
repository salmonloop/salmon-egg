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
    private readonly IGamepadNativeInputBridge _nativeInputBridge;

    public MainShellGamepadNavigationDispatcher(
        IShellBackNavigationService shellBackNavigation,
        IGamepadNativeInputBridge nativeInputBridge)
    {
        _shellBackNavigation = shellBackNavigation ?? throw new ArgumentNullException(nameof(shellBackNavigation));
        _nativeInputBridge = nativeInputBridge ?? throw new ArgumentNullException(nameof(nativeInputBridge));
    }

    public bool TryDispatch(GamepadNavigationIntent intent)
        => TryDispatchCore(intent, allowNativeFallback: true);

    public bool TryDispatchWithoutNativeFallback(GamepadNavigationIntent intent)
        => TryDispatchCore(intent, allowNativeFallback: false);

    private bool TryDispatchCore(GamepadNavigationIntent intent, bool allowNativeFallback)
    {
        if (TryConsumeNavigationIntent(intent))
        {
            return true;
        }

        if (allowNativeFallback && _nativeInputBridge.TryDispatch(intent))
        {
            return true;
        }

        return intent == GamepadNavigationIntent.Back
            && _shellBackNavigation.TryGoBack();
    }

    private static bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        var current = GetFocusedElement();
#if DEBUG
        App.BootLog($"GamepadDispatcher intent={intent} focusedChain={DescribeChain(current)}");
#endif
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

    private static string DescribeChain(DependencyObject? current)
    {
        if (current is null)
        {
            return "<null>";
        }

        var segments = new System.Collections.Generic.List<string>();
        while (current != null)
        {
            segments.Add(current.GetType().Name);
            current = VisualTreeHelper.GetParent(current);
        }

        return string.Join(">", segments);
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
