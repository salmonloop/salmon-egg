namespace SalmonEgg.Presentation.Core.Services.Input;

public static class RawGameControllerUnlabeledFaceIndexPolicy
{
    // Single Joy-Con HID layouts are not the common full-gamepad face/trigger index map.
    public static bool SupportsFullGamepadUnlabeledIndexFallback(string? displayName, ushort hardwareVendorId)
        => GamepadControllerIdentity.IsFullGamepadKnownFamily(displayName, hardwareVendorId);

    // Compatibility alias used by Windows wiring and existing call sites.
    public static bool SupportsFallback(string? displayName, ushort hardwareVendorId)
        => SupportsFullGamepadUnlabeledIndexFallback(displayName, hardwareVendorId);

    // Common HID face order for Xbox (A B X Y), DualShock/DualSense (Cross Circle Square Triangle),
    // and Switch Pro (B A Y X) is physical bottom/right/west/north.
    // Triggers on many full HID gamepads appear as digital buttons at indexes 6/7 when unlabeled.
    public static GamepadInputReading Apply(int buttonIndex, GamepadInputReading reading)
        => buttonIndex switch
        {
            0 => reading with { Activate = true },
            1 => reading with { Back = true },
            2 => reading,
            3 => reading with { ShortcutVoiceToggle = true },
            6 => reading with { LeftTrigger = 1 },
            7 => reading with { RightTrigger = 1 },
            _ => reading
        };
}
