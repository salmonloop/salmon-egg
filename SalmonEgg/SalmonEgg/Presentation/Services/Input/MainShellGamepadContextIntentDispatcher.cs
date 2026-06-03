using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services.Input;
using XamlFocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class MainShellGamepadContextIntentDispatcher : IGamepadContextIntentDispatcher
{
    private readonly ILogger<MainShellGamepadContextIntentDispatcher> _logger;

    public MainShellGamepadContextIntentDispatcher(ILogger<MainShellGamepadContextIntentDispatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool TryDispatch(GamepadContextIntent intent)
    {
        var focused = GetFocusedElement();
        if (TryDispatchFromRoot(focused, intent))
        {
            return true;
        }

        if (TryDispatchFromRoot(GetCurrentRootContent(), intent))
        {
            _logger.LogDebug(
                "Main shell gamepad context intent was retried from current root content after focused dispatch miss. Intent={Intent}.",
                intent);
            return true;
        }

        _logger.LogDebug("Main shell gamepad context intent not consumed. Intent={Intent}.", intent);
        return false;
    }

    private bool TryDispatchFromRoot(DependencyObject? current, GamepadContextIntent intent)
    {
        while (current is not null)
        {
            if (current is IGamepadContextIntentConsumer consumer
                && consumer.TryConsumeContextIntent(intent))
            {
                _logger.LogDebug(
                    "Main shell gamepad context intent consumed by UI consumer. Intent={Intent} ConsumerType={ConsumerType}.",
                    intent,
                    consumer.GetType().FullName);
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static DependencyObject? GetFocusedElement()
    {
        var root = App.MainWindowInstance?.Content as FrameworkElement;
        return root?.XamlRoot is null
            ? null
            : XamlFocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
    }

    private static DependencyObject? GetCurrentRootContent()
    {
        if (App.MainWindowInstance?.Content is not FrameworkElement { XamlRoot: not null } rootContent)
        {
            return null;
        }

        if (rootContent is Frame rootFrame && rootFrame.Content is DependencyObject frameContent)
        {
            return frameContent;
        }

        return rootContent;
    }
}
