namespace SalmonEgg.Presentation.Core.Services.Input;

/// <summary>
/// Projects W3C standard-mapping browser button indexes into portable pressed names
/// for diagnostics. Names follow the standard Gamepad button slots (A/B/X/Y/...);
/// face semantics remain position-based under <c>mapping === "standard"</c>.
/// </summary>
public static class BrowserStandardGamepadPressedButtons
{
    private const double ButtonPressedThreshold = 0.5;

    private static readonly string[] StandardNames =
    [
        "A",
        "B",
        "X",
        "Y",
        "LeftShoulder",
        "RightShoulder",
        "LeftTrigger",
        "RightTrigger",
        "View",
        "Menu",
        "LeftThumbstick",
        "RightThumbstick",
        "DPadUp",
        "DPadDown",
        "DPadLeft",
        "DPadRight"
    ];

    public static IReadOnlyList<string> GetPressedNames(
        string? mapping,
        IReadOnlyList<BrowserGamepadButtonReading> buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);

        if (!string.Equals(mapping, BrowserGamepadInputReadingMapper.StandardMapping, StringComparison.Ordinal))
        {
            return [];
        }

        var pressed = new List<string>();
        var limit = Math.Min(buttons.Count, StandardNames.Length);
        for (var index = 0; index < limit; index++)
        {
            var button = buttons[index];
            if (!button.Pressed && ClampUnit(button.Value) < ButtonPressedThreshold)
            {
                continue;
            }

            pressed.Add(StandardNames[index]);
        }

        return pressed;
    }

    private static double ClampUnit(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }
}
