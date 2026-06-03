using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services.Input;
using XamlFocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class MainShellGamepadShortcutDispatcher : IGamepadShortcutDispatcher
{
    private readonly ILogger<MainShellGamepadShortcutDispatcher> _logger;

    public MainShellGamepadShortcutDispatcher(ILogger<MainShellGamepadShortcutDispatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool TryDispatch(GamepadShortcutIntent intent)
    {
        var current = GetFocusedElement();
        while (current is not null)
        {
            if (current is IGamepadShortcutConsumer consumer
                && consumer.TryConsumeShortcutIntent(intent))
            {
                _logger.LogDebug(
                    "Main shell gamepad shortcut intent consumed by UI consumer. Intent={Intent} ConsumerType={ConsumerType}.",
                    intent,
                    consumer.GetType().FullName);
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        _logger.LogDebug("Main shell gamepad shortcut intent not consumed. Intent={Intent}.", intent);
        return false;
    }

    private static DependencyObject? GetFocusedElement()
    {
        var root = App.MainWindowInstance?.Content as FrameworkElement;
        return root?.XamlRoot is null
            ? null
            : XamlFocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
    }
}
