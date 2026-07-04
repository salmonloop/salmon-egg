using System;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class MainShellGamepadShortcutDispatcher : IGamepadShortcutDispatcher
{
    private readonly ILogger<MainShellGamepadShortcutDispatcher> _logger;
    private readonly IShellFocusScope _focusScope;

    public MainShellGamepadShortcutDispatcher(
        ILogger<MainShellGamepadShortcutDispatcher> logger,
        IShellFocusScope focusScope)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _focusScope = focusScope ?? throw new ArgumentNullException(nameof(focusScope));
    }

    public bool TryDispatch(GamepadShortcutIntent intent)
    {
        foreach (var ancestor in _focusScope.EnumerateAncestors(_focusScope.GetFocusedElement()))
        {
            if (ancestor is IGamepadShortcutConsumer consumer
                && consumer.TryConsumeShortcutIntent(intent))
            {
                _logger.LogDebug(
                    "Main shell gamepad shortcut intent consumed by UI consumer. Intent={Intent} ConsumerType={ConsumerType}.",
                    intent,
                    consumer.GetType().FullName);
                return true;
            }
        }

        _logger.LogDebug("Main shell gamepad shortcut intent not consumed. Intent={Intent}.", intent);
        return false;
    }
}
