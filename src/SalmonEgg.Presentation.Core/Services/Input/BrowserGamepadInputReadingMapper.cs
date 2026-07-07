namespace SalmonEgg.Presentation.Core.Services.Input;

public static class BrowserGamepadInputReadingMapper
{
    public const string StandardMapping = "standard";

    private const double ButtonPressedThreshold = 0.5;

    public static GamepadInputReading GetInputReading(
        string? mapping,
        IReadOnlyList<BrowserGamepadButtonReading> buttons,
        IReadOnlyList<double> axes)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        ArgumentNullException.ThrowIfNull(axes);

        if (!string.Equals(mapping, StandardMapping, StringComparison.Ordinal))
        {
            return default;
        }

        return StandardGamepadInputReadingMapper.GetInputReading(
            moveUp: IsButtonPressed(buttons, 12),
            moveDown: IsButtonPressed(buttons, 13),
            moveLeft: IsButtonPressed(buttons, 14),
            moveRight: IsButtonPressed(buttons, 15),
            activate: IsButtonPressed(buttons, 0),
            back: IsButtonPressed(buttons, 1),
            shortcutVoiceToggle: IsButtonPressed(buttons, 3),
            leftTrigger: GetButtonValue(buttons, 6),
            rightTrigger: GetButtonValue(buttons, 7),
            thumbstickX: GetAxisValue(axes, 0),
            thumbstickY: -GetAxisValue(axes, 1));
    }

    private static bool IsButtonPressed(IReadOnlyList<BrowserGamepadButtonReading> buttons, int index)
        => TryGetButton(buttons, index, out var button)
            && (button.Pressed || ClampUnit(button.Value) >= ButtonPressedThreshold);

    private static double GetButtonValue(IReadOnlyList<BrowserGamepadButtonReading> buttons, int index)
        => TryGetButton(buttons, index, out var button) ? button.Value : 0;

    private static bool TryGetButton(
        IReadOnlyList<BrowserGamepadButtonReading> buttons,
        int index,
        out BrowserGamepadButtonReading button)
    {
        if (index < buttons.Count)
        {
            button = buttons[index];
            return true;
        }

        button = default;
        return false;
    }

    private static double GetAxisValue(IReadOnlyList<double> axes, int index)
        => index < axes.Count ? axes[index] : 0;

    private static double ClampUnit(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }
}
