#if WINDOWS
using SalmonEgg.Presentation.Core.Services.Input;
using Windows.Gaming.Input;

namespace SalmonEgg.Presentation.Services.Input;

internal static class WindowsGameControllerButtonLabelMapper
{
    public static RawGameControllerButtonLabel Map(GameControllerButtonLabel label)
        => label switch
        {
            GameControllerButtonLabel.XboxUp => RawGameControllerButtonLabel.XboxUp,
            GameControllerButtonLabel.Up => RawGameControllerButtonLabel.Up,
            GameControllerButtonLabel.XboxDown => RawGameControllerButtonLabel.XboxDown,
            GameControllerButtonLabel.Down => RawGameControllerButtonLabel.Down,
            GameControllerButtonLabel.XboxLeft => RawGameControllerButtonLabel.XboxLeft,
            GameControllerButtonLabel.Left => RawGameControllerButtonLabel.Left,
            GameControllerButtonLabel.XboxRight => RawGameControllerButtonLabel.XboxRight,
            GameControllerButtonLabel.Right => RawGameControllerButtonLabel.Right,
            GameControllerButtonLabel.XboxA => RawGameControllerButtonLabel.XboxA,
            GameControllerButtonLabel.XboxB => RawGameControllerButtonLabel.XboxB,
            GameControllerButtonLabel.XboxX => RawGameControllerButtonLabel.XboxX,
            GameControllerButtonLabel.XboxY => RawGameControllerButtonLabel.XboxY,
            GameControllerButtonLabel.Cross => RawGameControllerButtonLabel.Cross,
            GameControllerButtonLabel.Circle => RawGameControllerButtonLabel.Circle,
            GameControllerButtonLabel.Square => RawGameControllerButtonLabel.Square,
            GameControllerButtonLabel.Triangle => RawGameControllerButtonLabel.Triangle,
            GameControllerButtonLabel.LetterA => RawGameControllerButtonLabel.LetterA,
            GameControllerButtonLabel.LetterB => RawGameControllerButtonLabel.LetterB,
            GameControllerButtonLabel.LetterX => RawGameControllerButtonLabel.LetterX,
            GameControllerButtonLabel.LetterY => RawGameControllerButtonLabel.LetterY,
            GameControllerButtonLabel.Back => RawGameControllerButtonLabel.Back,
            GameControllerButtonLabel.XboxLeftTrigger => RawGameControllerButtonLabel.XboxLeftTrigger,
            GameControllerButtonLabel.LeftTrigger => RawGameControllerButtonLabel.LeftTrigger,
            GameControllerButtonLabel.XboxRightTrigger => RawGameControllerButtonLabel.XboxRightTrigger,
            GameControllerButtonLabel.RightTrigger => RawGameControllerButtonLabel.RightTrigger,
            _ => RawGameControllerButtonLabel.None
        };
}
#endif
