#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Presentation.Core.Services.Input;
using Windows.Gaming.Input;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class WindowsGamepadDiagnosticsService : IGamepadDiagnosticsService
{
    private readonly WindowsRawGameControllerMapper _rawMapper;

    public WindowsGamepadDiagnosticsService(WindowsRawGameControllerMapper rawMapper)
    {
        _rawMapper = rawMapper ?? throw new ArgumentNullException(nameof(rawMapper));
    }

    public GamepadDiagnosticsSnapshot GetCurrentSnapshot()
    {
        var gamepads = Gamepad.Gamepads.ToArray();
        var rawControllers = RawGameController.RawGameControllers.ToArray();

        var source = GamepadDiagnosticsInputSource.None;
        var reading = default(GamepadInputReading);

        foreach (var gamepad in gamepads)
        {
            reading = GetInputReading(gamepad.GetCurrentReading());
            if (GamepadIntentProcessor.GetActiveIntents(reading).Count > 0
                || GamepadContextIntentProjector.HasActiveIntents(reading)
                || GamepadShortcutIntentProjector.HasActiveShortcuts(reading))
            {
                source = GamepadDiagnosticsInputSource.Gamepad;
                break;
            }
        }

        if (source == GamepadDiagnosticsInputSource.None)
        {
            foreach (var controller in rawControllers)
            {
                reading = _rawMapper.GetInputReading(controller);
                if (GamepadIntentProcessor.GetActiveIntents(reading).Count > 0
                    || GamepadContextIntentProjector.HasActiveIntents(reading)
                    || GamepadShortcutIntentProjector.HasActiveShortcuts(reading))
                {
                    source = GamepadDiagnosticsInputSource.RawGameController;
                    break;
                }
            }
        }

        var activeIntents = GamepadIntentProcessor.GetActiveIntents(reading);
        var activeContextIntents = GamepadContextIntentProjector.GetActiveIntents(reading);
        return new GamepadDiagnosticsSnapshot(
            IsSupported: true,
            ConnectedGamepadCount: gamepads.Length,
            ConnectedRawControllerCount: rawControllers.Length,
            InputSource: source,
            Reading: reading,
            ActiveIntents: activeIntents,
            ActiveContextIntents: activeContextIntents,
            RawControllers: rawControllers.Select(CreateRawControllerDiagnostics).ToArray());
    }

    private static RawGameControllerDiagnostics CreateRawControllerDiagnostics(RawGameController controller)
    {
        var buttons = new bool[controller.ButtonCount];
        var switches = new GameControllerSwitchPosition[controller.SwitchCount];
        var axes = new double[controller.AxisCount];
        controller.GetCurrentReading(buttons, switches, axes);

        return new RawGameControllerDiagnostics(
            DisplayName: controller.DisplayName,
            HardwareVendorId: controller.HardwareVendorId,
            HardwareProductId: controller.HardwareProductId,
            IsWireless: controller.IsWireless,
            ButtonCount: controller.ButtonCount,
            SwitchCount: controller.SwitchCount,
            AxisCount: controller.AxisCount,
            PressedButtons: GetPressedButtons(controller, buttons),
            ActiveSwitches: GetActiveSwitches(switches),
            Axes: axes);
    }

    private static string[] GetPressedButtons(RawGameController controller, IReadOnlyList<bool> buttons)
    {
        var pressedButtons = new List<string>();
        for (var i = 0; i < buttons.Count; i++)
        {
            if (!buttons[i])
            {
                continue;
            }

            var label = controller.GetButtonLabel(i);
            pressedButtons.Add(label == GameControllerButtonLabel.None
                ? $"B{i}"
                : $"B{i}:{label}");
        }

        return pressedButtons.ToArray();
    }

    private static string[] GetActiveSwitches(IReadOnlyList<GameControllerSwitchPosition> switches)
    {
        var activeSwitches = new List<string>();
        for (var i = 0; i < switches.Count; i++)
        {
            var position = switches[i];
            if (position != GameControllerSwitchPosition.Center)
            {
                activeSwitches.Add($"S{i}:{position}");
            }
        }

        return activeSwitches.ToArray();
    }

    private static GamepadInputReading GetInputReading(GamepadReading reading)
    {
        return StandardGamepadInputReadingMapper.GetInputReading(
            moveUp: reading.Buttons.HasFlag(GamepadButtons.DPadUp),
            moveDown: reading.Buttons.HasFlag(GamepadButtons.DPadDown),
            moveLeft: reading.Buttons.HasFlag(GamepadButtons.DPadLeft),
            moveRight: reading.Buttons.HasFlag(GamepadButtons.DPadRight),
            activate: reading.Buttons.HasFlag(GamepadButtons.A),
            back: reading.Buttons.HasFlag(GamepadButtons.B),
            shortcutVoiceToggle: reading.Buttons.HasFlag(GamepadButtons.Y),
            leftTrigger: reading.LeftTrigger,
            rightTrigger: reading.RightTrigger,
            thumbstickX: reading.LeftThumbstickX,
            thumbstickY: reading.LeftThumbstickY);
    }
}
#endif
