#if WINDOWS
using System;
using SalmonEgg.Presentation.Core.Services.Input;
using Windows.Gaming.Input;

namespace SalmonEgg.Presentation.Services.Input;

internal readonly record struct WindowsStandardGamepadIdentity(
    string DisplayName,
    ushort? HardwareVendorId,
    ushort? HardwareProductId)
{
    public static WindowsStandardGamepadIdentity Empty { get; } = new(
        DisplayName: string.Empty,
        HardwareVendorId: null,
        HardwareProductId: null);
}

internal static class WindowsGameControllerButtonLabelMapper
{
    public static StandardGamepadFaceButtonLabels GetFaceButtonLabels(Gamepad gamepad)
    {
        ArgumentNullException.ThrowIfNull(gamepad);
        return new(
            A: Map(gamepad.GetButtonLabel(GamepadButtons.A)),
            B: Map(gamepad.GetButtonLabel(GamepadButtons.B)),
            X: Map(gamepad.GetButtonLabel(GamepadButtons.X)),
            Y: Map(gamepad.GetButtonLabel(GamepadButtons.Y)));
    }

    public static WindowsStandardGamepadIdentity GetIdentity(Gamepad gamepad)
    {
        ArgumentNullException.ThrowIfNull(gamepad);

        try
        {
            var raw = RawGameController.FromGameController(gamepad);
            if (raw is null)
            {
                return WindowsStandardGamepadIdentity.Empty;
            }

            return new WindowsStandardGamepadIdentity(
                DisplayName: raw.DisplayName ?? string.Empty,
                HardwareVendorId: raw.HardwareVendorId,
                HardwareProductId: raw.HardwareProductId);
        }
        catch (Exception)
        {
            // Identity is optional enrichment for diagnostics/layout. Never fail live
            // standard-path polling when FromGameController is unavailable for a device.
            return WindowsStandardGamepadIdentity.Empty;
        }
    }

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
