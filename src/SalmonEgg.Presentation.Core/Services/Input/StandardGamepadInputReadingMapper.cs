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
        => GetInputReading(
            moveUp,
            moveDown,
            moveLeft,
            moveRight,
            faceAPressed: activate,
            faceBPressed: back,
            faceXPressed: false,
            faceYPressed: shortcutVoiceToggle,
            leftTrigger,
            rightTrigger,
            thumbstickX,
            thumbstickY,
            labels: default);

    public static GamepadInputReading GetInputReading(
        bool moveUp,
        bool moveDown,
        bool moveLeft,
        bool moveRight,
        bool faceAPressed,
        bool faceBPressed,
        bool faceXPressed,
        bool faceYPressed,
        double leftTrigger,
        double rightTrigger,
        double thumbstickX,
        double thumbstickY,
        StandardGamepadFaceButtonLabels labels,
        string? displayName = null,
        ushort? hardwareVendorId = null)
    {
        var reading = new GamepadInputReading(
            MoveUp: moveUp,
            MoveDown: moveDown,
            MoveLeft: moveLeft,
            MoveRight: moveRight,
            Activate: false,
            Back: false,
            ShortcutVoiceToggle: false,
            LeftTrigger: ClampUnit(leftTrigger),
            RightTrigger: ClampUnit(rightTrigger),
            ThumbstickX: ClampSigned(thumbstickX),
            ThumbstickY: ClampSigned(thumbstickY));

        // Prefer controller identity when available so standard-path layout matches
        // diagnostics and raw layout resolution for the same device facts.
        var faceButtonLayout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            displayName,
            hardwareVendorId,
            labels);
        if (faceAPressed)
        {
            reading = ApplyFaceButton(reading, labels.A, faceButtonLayout, activateFallback: true, backFallback: false, voiceFallback: false);
        }

        if (faceBPressed)
        {
            reading = ApplyFaceButton(reading, labels.B, faceButtonLayout, activateFallback: false, backFallback: true, voiceFallback: false);
        }

        if (faceXPressed)
        {
            reading = ApplyFaceButton(reading, labels.X, faceButtonLayout, activateFallback: false, backFallback: false, voiceFallback: false);
        }

        if (faceYPressed)
        {
            reading = ApplyFaceButton(reading, labels.Y, faceButtonLayout, activateFallback: false, backFallback: false, voiceFallback: true);
        }

        return reading;
    }

    private static GamepadInputReading ApplyFaceButton(
        GamepadInputReading reading,
        RawGameControllerButtonLabel label,
        RawGameControllerFaceButtonLayout faceButtonLayout,
        bool activateFallback,
        bool backFallback,
        bool voiceFallback)
    {
        if (label != RawGameControllerButtonLabel.None)
        {
            return RawGameControllerButtonLabelMapper.Apply(label, reading, faceButtonLayout);
        }

        if (activateFallback)
        {
            return reading with { Activate = true };
        }

        if (backFallback)
        {
            return reading with { Back = true };
        }

        if (voiceFallback)
        {
            return reading with { ShortcutVoiceToggle = true };
        }

        return reading;
    }

    private static double ClampUnit(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }

    private static double ClampSigned(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, -1, 1);
    }
}
