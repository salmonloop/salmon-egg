using System;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class MainShellGamepadContextIntentDispatcher : IGamepadContextIntentDispatcher
{
    private readonly ILogger<MainShellGamepadContextIntentDispatcher> _logger;
    private readonly IShellFocusScope _focusScope;

    public MainShellGamepadContextIntentDispatcher(
        ILogger<MainShellGamepadContextIntentDispatcher> logger,
        IShellFocusScope focusScope)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _focusScope = focusScope ?? throw new ArgumentNullException(nameof(focusScope));
    }

    public bool TryDispatch(GamepadContextIntent intent)
    {
        if (TryDispatchFromRoot(_focusScope.GetFocusedElement(), intent))
        {
            return true;
        }

        if (TryDispatchFromRoot(_focusScope.GetCurrentRootContent(), intent))
        {
            _logger.LogDebug(
                "Main shell gamepad context intent was retried from current root content after focused dispatch miss. Intent={Intent}.",
                intent);
            return true;
        }

        _logger.LogDebug("Main shell gamepad context intent not consumed. Intent={Intent}.", intent);
        return false;
    }

    private bool TryDispatchFromRoot(DependencyObject? root, GamepadContextIntent intent)
    {
        foreach (var ancestor in _focusScope.EnumerateAncestors(root))
        {
            if (ancestor is IGamepadContextIntentConsumer consumer
                && consumer.TryConsumeContextIntent(intent))
            {
                _logger.LogDebug(
                    "Main shell gamepad context intent consumed by UI consumer. Intent={Intent} ConsumerType={ConsumerType}.",
                    intent,
                    consumer.GetType().FullName);
                return true;
            }
        }

        return false;
    }
}
