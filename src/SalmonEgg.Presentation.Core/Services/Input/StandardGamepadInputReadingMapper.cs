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
        StandardGamepadFaceButtonLabels labels)
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

        var faceButtonLayout = ResolveFaceButtonLayout(labels);
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

    private static RawGameControllerFaceButtonLayout ResolveFaceButtonLayout(StandardGamepadFaceButtonLabels labels)
    {
        if (IsLetterLabel(labels.A)
            || IsLetterLabel(labels.B)
            || IsLetterLabel(labels.X)
            || IsLetterLabel(labels.Y))
        {
            return RawGameControllerFaceButtonLayout.Nintendo;
        }

        return RawGameControllerFaceButtonLayout.Standard;
    }

    private static bool IsLetterLabel(RawGameControllerButtonLabel label)
        => label is RawGameControllerButtonLabel.LetterA
            or RawGameControllerButtonLabel.LetterB
            or RawGameControllerButtonLabel.LetterX
            or RawGameControllerButtonLabel.LetterY;

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
