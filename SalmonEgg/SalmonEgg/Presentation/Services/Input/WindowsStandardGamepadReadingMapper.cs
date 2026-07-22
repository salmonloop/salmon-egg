#if WINDOWS
using System;
using SalmonEgg.Presentation.Core.Services.Input;
using Windows.Gaming.Input;

namespace SalmonEgg.Presentation.Services.Input;

/// <summary>
/// Shared Windows standard-path fact → Core reading mapping used by live input and Diagnostics.
/// Keeps WGI button flag extraction in one place without inventing a connection host.
/// </summary>
internal static class WindowsStandardGamepadReadingMapper
{
    public static GamepadInputReading GetInputReading(
        Gamepad gamepad,
        WindowsStandardGamepadIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(gamepad);

        var reading = gamepad.GetCurrentReading();
        var labels = WindowsGameControllerButtonLabelMapper.GetFaceButtonLabels(gamepad);
        return GetInputReading(gamepad, reading, labels, identity);
    }

    public static GamepadInputReading GetInputReading(
        Gamepad gamepad,
        GamepadReading reading,
        StandardGamepadFaceButtonLabels labels,
        WindowsStandardGamepadIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(gamepad);

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
            labels: labels,
            displayName: identity.DisplayName,
            hardwareVendorId: identity.HardwareVendorId);
    }
}
#endif
