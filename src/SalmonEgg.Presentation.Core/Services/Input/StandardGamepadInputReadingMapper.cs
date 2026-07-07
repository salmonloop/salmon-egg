namespace SalmonEgg.Presentation.Core.Services.Input;

public static class StandardGamepadInputReadingMapper
{
    public static GamepadInputReading GetInputReading(
        bool moveUp,
        bool moveDown,
        bool moveLeft,
        bool moveRight,
        bool activate,
        bool back,
        bool shortcutVoiceToggle,
        double leftTrigger,
        double rightTrigger,
        double thumbstickX,
        double thumbstickY)
    {
        return new GamepadInputReading(
            MoveUp: moveUp,
            MoveDown: moveDown,
            MoveLeft: moveLeft,
            MoveRight: moveRight,
            Activate: activate,
            Back: back,
            ShortcutVoiceToggle: shortcutVoiceToggle,
            LeftTrigger: ClampUnit(leftTrigger),
            RightTrigger: ClampUnit(rightTrigger),
            ThumbstickX: ClampSigned(thumbstickX),
            ThumbstickY: ClampSigned(thumbstickY));
    }

    private static double ClampUnit(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }

    private static double ClampSigned(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp(value, -1, 1);
    }
}
