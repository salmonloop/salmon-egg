using System.Collections.Generic;

namespace SalmonEgg.Presentation.Core.Services.Input;

public static class GamepadContextIntentProjector
{
    internal const double TriggerPressedThreshold = 0.5;
    private static readonly GamepadContextIntent[] PageUpIntent = [GamepadContextIntent.PageUp];
    private static readonly GamepadContextIntent[] PageDownIntent = [GamepadContextIntent.PageDown];
    private static readonly GamepadContextIntent[] BothPageIntents =
    [
        GamepadContextIntent.PageUp,
        GamepadContextIntent.PageDown
    ];

    public static bool HasActiveIntents(GamepadInputReading reading)
        => reading.LeftTrigger >= TriggerPressedThreshold
            || reading.RightTrigger >= TriggerPressedThreshold;

    public static IReadOnlyCollection<GamepadContextIntent> GetActiveIntents(GamepadInputReading reading)
    {
        var leftPressed = reading.LeftTrigger >= TriggerPressedThreshold;
        var rightPressed = reading.RightTrigger >= TriggerPressedThreshold;

        if (leftPressed && rightPressed)
        {
            return BothPageIntents;
        }

        if (leftPressed)
        {
            return PageUpIntent;
        }

        if (rightPressed)
        {
            return PageDownIntent;
        }

        return [];
    }
}
