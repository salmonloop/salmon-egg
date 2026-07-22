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

        var pressedButtons = new List<RawGameControllerButtonPress>();
        var faceButtonLayout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            controller.DisplayName,
            controller.HardwareVendorId);
        var allowUnlabeledFaceIndexFallback = RawGameControllerUnlabeledFaceIndexPolicy.SupportsFullGamepadUnlabeledIndexFallback(
            controller.DisplayName,
            controller.HardwareVendorId);

        for (var i = 0; i < buttons.Count; i++)
        {
            if (!buttons[i])
            {
                continue;
            }

            pressedButtons.Add(new RawGameControllerButtonPress(
                Index: i,
                Label: WindowsGameControllerButtonLabelMapper.Map(controller.GetButtonLabel(i))));
        }

        var switchPositions = new GamepadDirectionalSwitchPosition[switches.Count];
        for (var i = 0; i < switches.Count; i++)
        {
            switchPositions[i] = (GamepadDirectionalSwitchPosition)(int)switches[i];
        }

        return RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            pressedButtons,
            switchPositions,
            axes,
            faceButtonLayout,
            allowUnlabeledFaceIndexFallback);
    }
}
#endif
