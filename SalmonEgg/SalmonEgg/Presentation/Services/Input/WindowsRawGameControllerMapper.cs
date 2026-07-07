#if WINDOWS
using System;
using System.Collections.Generic;
using SalmonEgg.Presentation.Core.Services.Input;
using Windows.Gaming.Input;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class WindowsRawGameControllerMapper
{
    public HashSet<GamepadNavigationIntent> GetActiveIntents(RawGameController controller)
    {
        return GamepadIntentProcessor.GetActiveIntents(GetInputReading(controller));
    }

    public GamepadInputReading GetInputReading(RawGameController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var buttons = new bool[controller.ButtonCount];
        var switches = new GameControllerSwitchPosition[controller.SwitchCount];
        var axes = new double[controller.AxisCount];
        controller.GetCurrentReading(buttons, switches, axes);

        var pressedButtonLabels = new List<RawGameControllerButtonLabel>();

        for (var i = 0; i < buttons.Length; i++)
        {
            if (!buttons[i])
            {
                continue;
            }

            pressedButtonLabels.Add(MapButtonLabel(controller.GetButtonLabel(i)));
        }

        var switchPositions = Array.ConvertAll(
            switches,
            static position => (GamepadDirectionalSwitchPosition)(int)position);

        return RawGameControllerInputReadingMapper.GetInputReading(pressedButtonLabels, switchPositions, axes);
    }

    private static RawGameControllerButtonLabel MapButtonLabel(GameControllerButtonLabel label)
    {
        return label switch
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
            GameControllerButtonLabel.Cross => RawGameControllerButtonLabel.Cross,
            GameControllerButtonLabel.LetterA => RawGameControllerButtonLabel.LetterA,
            GameControllerButtonLabel.XboxB => RawGameControllerButtonLabel.XboxB,
            GameControllerButtonLabel.Circle => RawGameControllerButtonLabel.Circle,
            GameControllerButtonLabel.LetterB => RawGameControllerButtonLabel.LetterB,
            GameControllerButtonLabel.Back => RawGameControllerButtonLabel.Back,
            GameControllerButtonLabel.XboxY => RawGameControllerButtonLabel.XboxY,
            GameControllerButtonLabel.Triangle => RawGameControllerButtonLabel.Triangle,
            GameControllerButtonLabel.LetterY => RawGameControllerButtonLabel.LetterY,
            GameControllerButtonLabel.XboxLeftTrigger => RawGameControllerButtonLabel.XboxLeftTrigger,
            GameControllerButtonLabel.LeftTrigger => RawGameControllerButtonLabel.LeftTrigger,
            GameControllerButtonLabel.XboxRightTrigger => RawGameControllerButtonLabel.XboxRightTrigger,
            GameControllerButtonLabel.RightTrigger => RawGameControllerButtonLabel.RightTrigger,
            _ => RawGameControllerButtonLabel.None
        };
    }
}
#endif
