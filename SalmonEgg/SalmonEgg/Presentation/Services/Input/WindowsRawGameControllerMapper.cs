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

        return GetInputReading(controller, buttons, switches, axes);
    }

    public GamepadInputReading GetInputReading(
        RawGameController controller,
        IReadOnlyList<bool> buttons,
        IReadOnlyList<GameControllerSwitchPosition> switches,
        IReadOnlyList<double> axes)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(buttons);
        ArgumentNullException.ThrowIfNull(switches);
        ArgumentNullException.ThrowIfNull(axes);

        var pressedButtonLabels = new List<RawGameControllerButtonLabel>();
        var faceButtonLayout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            controller.DisplayName,
            controller.HardwareVendorId);

        for (var i = 0; i < buttons.Count; i++)
        {
            if (!buttons[i])
            {
                continue;
            }

            pressedButtonLabels.Add(MapButtonLabel(controller.GetButtonLabel(i)));
        }

        var switchPositions = new GamepadDirectionalSwitchPosition[switches.Count];
        for (var i = 0; i < switches.Count; i++)
        {
            switchPositions[i] = (GamepadDirectionalSwitchPosition)(int)switches[i];
        }

        return RawGameControllerInputReadingMapper.GetInputReading(
            pressedButtonLabels,
            switchPositions,
            axes,
            faceButtonLayout);
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
}
#endif
