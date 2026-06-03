using System;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<MainShellGamepadNavigationDispatcher> _logger;

    public MainShellGamepadNavigationDispatcher(
        IShellBackNavigationService shellBackNavigation,
        IGamepadNativeInputBridge nativeInputBridge,
        ILogger<MainShellGamepadNavigationDispatcher> logger)
    {
        _shellBackNavigation = shellBackNavigation ?? throw new ArgumentNullException(nameof(shellBackNavigation));
        _nativeInputBridge = nativeInputBridge ?? throw new ArgumentNullException(nameof(nativeInputBridge));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool TryDispatch(GamepadNavigationIntent intent)
        => TryDispatchCore(intent, allowNativeFallback: true);

    public bool TryDispatchWithoutNativeFallback(GamepadNavigationIntent intent)
        => TryDispatchCore(intent, allowNativeFallback: false);

    private bool TryDispatchCore(GamepadNavigationIntent intent, bool allowNativeFallback)
    {
        _logger.LogDebug(
            "Main shell gamepad navigation dispatch started. Intent={Intent} AllowNativeFallback={AllowNativeFallback}.",
            intent,
            allowNativeFallback);

        if (TryConsumeNavigationIntent(intent))
        {
            _logger.LogDebug("Main shell gamepad navigation consumed by focused consumer. Intent={Intent}.", intent);
            return true;
        }

        if (allowNativeFallback && _nativeInputBridge.TryDispatch(intent))
        {
            _logger.LogDebug("Main shell gamepad navigation fallback to native input bridge succeeded. Intent={Intent}.", intent);
            return true;
        }

        if (intent != GamepadNavigationIntent.Back)
        {
            _logger.LogDebug("Main shell gamepad navigation not consumed and no fallback consumed. Intent={Intent}.", intent);
            return false;
        }

        var shellBack = _shellBackNavigation.TryGoBack();
        if (shellBack)
        {
            _logger.LogDebug("Main shell gamepad navigation fallback to shell back succeeded. Intent={Intent}.", intent);
        }
        else
        {
            _logger.LogDebug("Main shell gamepad navigation fallback to shell back failed. Intent={Intent}.", intent);
        }

        return shellBack;
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
