namespace SalmonEgg.Presentation.Core.Services.Input;

public static class RawGameControllerButtonLabelMapper
{
    public static GamepadInputReading Apply(RawGameControllerButtonLabel label, GamepadInputReading reading)
    {
        return label switch
        {
            RawGameControllerButtonLabel.XboxUp or RawGameControllerButtonLabel.Up => reading with { MoveUp = true },
            RawGameControllerButtonLabel.XboxDown or RawGameControllerButtonLabel.Down => reading with { MoveDown = true },
            RawGameControllerButtonLabel.XboxLeft or RawGameControllerButtonLabel.Left => reading with { MoveLeft = true },
            RawGameControllerButtonLabel.XboxRight or RawGameControllerButtonLabel.Right => reading with { MoveRight = true },
            RawGameControllerButtonLabel.XboxA or RawGameControllerButtonLabel.Cross or RawGameControllerButtonLabel.LetterA => reading with { Activate = true },
            RawGameControllerButtonLabel.XboxB or RawGameControllerButtonLabel.Circle or RawGameControllerButtonLabel.LetterB or RawGameControllerButtonLabel.Back => reading with { Back = true },
            RawGameControllerButtonLabel.XboxY or RawGameControllerButtonLabel.Triangle or RawGameControllerButtonLabel.LetterY => reading with { ShortcutVoiceToggle = true },
            RawGameControllerButtonLabel.XboxLeftTrigger or RawGameControllerButtonLabel.LeftTrigger => reading with { LeftTrigger = 1 },
            RawGameControllerButtonLabel.XboxRightTrigger or RawGameControllerButtonLabel.RightTrigger => reading with { RightTrigger = 1 },
            _ => reading
        };
    }
}
