#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Presentation.Core.Services.Input;
using Windows.Gaming.Input;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class WindowsGamepadDiagnosticsService : IGamepadDiagnosticsService
{
    private static readonly GamepadButtons[] DiagnosticButtons =
    [
        GamepadButtons.A,
        GamepadButtons.B,
        GamepadButtons.X,
        GamepadButtons.Y,
        GamepadButtons.DPadUp,
        GamepadButtons.DPadDown,
        GamepadButtons.DPadLeft,
        GamepadButtons.DPadRight,
        GamepadButtons.LeftShoulder,
        GamepadButtons.RightShoulder,
        GamepadButtons.LeftThumbstick,
        GamepadButtons.RightThumbstick,
        GamepadButtons.Menu,
        GamepadButtons.View
    ];

    private readonly WindowsRawGameControllerMapper _rawMapper;

    public WindowsGamepadDiagnosticsService(WindowsRawGameControllerMapper rawMapper)
    {
        _rawMapper = rawMapper ?? throw new ArgumentNullException(nameof(rawMapper));
    }

    public GamepadDiagnosticsSnapshot GetCurrentSnapshot()
    {
        var standardGamepads = Gamepad.Gamepads.Select(CreateStandardGamepadDiagnostics).ToArray();
        var rawControllers = RawGameController.RawGameControllers.Select(CreateRawControllerDiagnostics).ToArray();

        var source = GamepadDiagnosticsInputSource.None;
        var reading = default(GamepadInputReading);

        foreach (var diagnostics in standardGamepads)
        {
            reading = diagnostics.Reading;
            if (HasActiveInput(reading))
            {
                source = GamepadDiagnosticsInputSource.Gamepad;
                break;
            }
        }

        if (source == GamepadDiagnosticsInputSource.None)
        {
            foreach (var diagnostics in rawControllers)
            {
                reading = diagnostics.Reading;
                if (HasActiveInput(reading))
                {
                    source = GamepadDiagnosticsInputSource.RawGameController;
                    break;
                }
            }
        }

        var activeIntents = GamepadIntentProcessor.GetActiveIntents(reading);
        var activeContextIntents = GamepadContextIntentProjector.GetActiveIntents(reading);
        var activeShortcuts = GamepadShortcutIntentProjector.GetActiveShortcuts(reading);
        return new GamepadDiagnosticsSnapshot(
            IsSupported: true,
            ConnectedGamepadCount: standardGamepads.Length,
            ConnectedRawControllerCount: rawControllers.Length,
            InputSource: source,
            Reading: reading,
            ActiveIntents: activeIntents,
            ActiveContextIntents: activeContextIntents,
            ActiveShortcuts: activeShortcuts,
            StandardGamepads: standardGamepads,
            RawControllers: rawControllers);
    }

    private static bool HasActiveInput(GamepadInputReading reading)
        => GamepadIntentProcessor.GetActiveIntents(reading).Count > 0
            || GamepadContextIntentProjector.HasActiveIntents(reading)
            || GamepadShortcutIntentProjector.HasActiveShortcuts(reading);

    private static StandardGamepadDiagnostics CreateStandardGamepadDiagnostics(Gamepad gamepad)
    {
        var reading = gamepad.GetCurrentReading();
        var labels = GetFaceButtonLabels(gamepad);
        var identity = WindowsGameControllerButtonLabelMapper.GetIdentity(gamepad);
        return new StandardGamepadDiagnostics(
            DisplayName: identity.DisplayName,
            HardwareVendorId: identity.HardwareVendorId,
            HardwareProductId: identity.HardwareProductId,
            FaceButtonLayout: RawGameControllerFaceButtonLayoutResolver.Resolve(
                identity.DisplayName,
                identity.HardwareVendorId,
                labels),
            ButtonLabels: GetButtonLabels(gamepad),
            PressedButtons: GetPressedButtons(reading.Buttons),
            Reading: GetInputReading(gamepad, reading, labels));
    }

    private static string[] GetButtonLabels(Gamepad gamepad)
    {
        var labels = new List<string>(DiagnosticButtons.Length);
        foreach (var button in DiagnosticButtons)
        {
            var label = gamepad.GetButtonLabel(button);
            labels.Add(label == GameControllerButtonLabel.None
                ? $"{button}:None"
                : $"{button}:{label}");
        }

        return labels.ToArray();
    }

    private static string[] GetPressedButtons(GamepadButtons buttons)
    {
        var pressedButtons = new List<string>();
        foreach (var button in DiagnosticButtons)
        {
            if (buttons.HasFlag(button))
            {
                pressedButtons.Add(button.ToString());
            }
        }

        return pressedButtons.ToArray();
    }

    private RawGameControllerDiagnostics CreateRawControllerDiagnostics(RawGameController controller)
    {
        var buttons = new bool[controller.ButtonCount];
        var switches = new GameControllerSwitchPosition[controller.SwitchCount];
        var axes = new double[controller.AxisCount];
        controller.GetCurrentReading(buttons, switches, axes);
        var reading = _rawMapper.GetInputReading(controller, buttons, switches, axes);

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
            Axes: axes,
            Reading: reading);
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

    private static GamepadInputReading GetInputReading(
        Gamepad gamepad,
        GamepadReading reading,
        StandardGamepadFaceButtonLabels labels)
    {
        return StandardGamepadInputReadingMapper.GetInputReading(
            moveUp: reading.Buttons.HasFlag(GamepadButtons.DPadUp),
            moveDown: reading.Buttons.HasFlag(GamepadButtons.DPadDown),
            moveLeft: reading.Buttons.HasFlag(GamepadButtons.DPadLeft),
            moveRight: reading.Buttons.HasFlag(GamepadButtons.DPadRight),
            faceAPressed: reading.Buttons.HasFlag(GamepadButtons.A),
            faceBPressed: reading.Buttons.HasFlag(GamepadButtons.B),
            faceXPressed: reading.Buttons.HasFlag(GamepadButtons.X),
            faceYPressed: reading.Buttons.HasFlag(GamepadButtons.Y),
            leftTrigger: reading.LeftTrigger,
            rightTrigger: reading.RightTrigger,
            thumbstickX: reading.LeftThumbstickX,
            thumbstickY: reading.LeftThumbstickY,
            labels: labels);
    }

    private static StandardGamepadFaceButtonLabels GetFaceButtonLabels(Gamepad gamepad)
        => WindowsGameControllerButtonLabelMapper.GetFaceButtonLabels(gamepad);
}
#endif
