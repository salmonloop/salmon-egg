namespace SalmonEgg.Presentation.Core.Services.Input;

public static class RawGameControllerButtonLabelMapper
{
    public static GamepadInputReading Apply(RawGameControllerButtonLabel label, GamepadInputReading reading)
        => Apply(label, reading, RawGameControllerFaceButtonLayout.Standard);

    public static GamepadInputReading Apply(
        RawGameControllerButtonLabel label,
        GamepadInputReading reading,
        RawGameControllerFaceButtonLayout faceButtonLayout)
    {
        return label switch
        {
            RawGameControllerButtonLabel.XboxUp or RawGameControllerButtonLabel.Up => reading with { MoveUp = true },
            RawGameControllerButtonLabel.XboxDown or RawGameControllerButtonLabel.Down => reading with { MoveDown = true },
            RawGameControllerButtonLabel.XboxLeft or RawGameControllerButtonLabel.Left => reading with { MoveLeft = true },
            RawGameControllerButtonLabel.XboxRight or RawGameControllerButtonLabel.Right => reading with { MoveRight = true },
            RawGameControllerButtonLabel.XboxA or RawGameControllerButtonLabel.Cross => reading with { Activate = true },
            RawGameControllerButtonLabel.XboxB or RawGameControllerButtonLabel.Circle or RawGameControllerButtonLabel.Back => reading with { Back = true },
            RawGameControllerButtonLabel.XboxY or RawGameControllerButtonLabel.Triangle => reading with { ShortcutVoiceToggle = true },
            RawGameControllerButtonLabel.LetterA => ApplyLetterA(reading, faceButtonLayout),
            RawGameControllerButtonLabel.LetterB => ApplyLetterB(reading, faceButtonLayout),
            RawGameControllerButtonLabel.LetterX => ApplyLetterX(reading, faceButtonLayout),
            RawGameControllerButtonLabel.LetterY => ApplyLetterY(reading, faceButtonLayout),
            RawGameControllerButtonLabel.XboxLeftTrigger or RawGameControllerButtonLabel.LeftTrigger => reading with { LeftTrigger = 1 },
            RawGameControllerButtonLabel.XboxRightTrigger or RawGameControllerButtonLabel.RightTrigger => reading with { RightTrigger = 1 },
            _ => reading
        };
    }

    private static GamepadInputReading ApplyLetterA(
        GamepadInputReading reading,
        RawGameControllerFaceButtonLayout faceButtonLayout)
        => faceButtonLayout == RawGameControllerFaceButtonLayout.Nintendo
            ? reading with { Back = true }
            : reading with { Activate = true };

    private static GamepadInputReading ApplyLetterB(
        GamepadInputReading reading,
        RawGameControllerFaceButtonLayout faceButtonLayout)
        => faceButtonLayout == RawGameControllerFaceButtonLayout.Nintendo
            ? reading with { Activate = true }
            : reading with { Back = true };

    private static GamepadInputReading ApplyLetterX(
        GamepadInputReading reading,
        RawGameControllerFaceButtonLayout faceButtonLayout)
        => faceButtonLayout == RawGameControllerFaceButtonLayout.Nintendo
            ? reading with { ShortcutVoiceToggle = true }
            : reading;

    private static GamepadInputReading ApplyLetterY(
        GamepadInputReading reading,
        RawGameControllerFaceButtonLayout faceButtonLayout)
        => faceButtonLayout == RawGameControllerFaceButtonLayout.Nintendo
            ? reading
            : reading with { ShortcutVoiceToggle = true };
}
