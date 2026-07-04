using System;
using Microsoft.UI.Xaml;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Services.Navigation;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class MainShellGamepadNavigationDispatcher : IGamepadNavigationDispatcher
{
    private readonly IShellBackNavigationService _shellBackNavigation;
    private readonly IGamepadNativeInputBridge _nativeInputBridge;
    private readonly IShellFocusScope _focusScope;

    public MainShellGamepadNavigationDispatcher(
        IShellBackNavigationService shellBackNavigation,
        IGamepadNativeInputBridge nativeInputBridge,
        IShellFocusScope focusScope)
    {
        _shellBackNavigation = shellBackNavigation ?? throw new ArgumentNullException(nameof(shellBackNavigation));
        _nativeInputBridge = nativeInputBridge ?? throw new ArgumentNullException(nameof(nativeInputBridge));
        _focusScope = focusScope ?? throw new ArgumentNullException(nameof(focusScope));
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

        if (intent != GamepadNavigationIntent.Back)
        {
            return false;
        }

        return _shellBackNavigation.TryGoBack();
    }

    private bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        foreach (var ancestor in _focusScope.EnumerateAncestors(_focusScope.GetFocusedElement()))
        {
            if (ancestor is INavigationIntentConsumer consumer
                && consumer.TryConsumeNavigationIntent(intent))
            {
                return true;
            }
        }

        return false;
    }
}
