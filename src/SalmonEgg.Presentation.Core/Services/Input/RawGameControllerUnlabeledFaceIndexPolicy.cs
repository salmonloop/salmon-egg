namespace SalmonEgg.Presentation.Core.Services.Input;

public static class RawGameControllerUnlabeledFaceIndexPolicy
{
    // Single Joy-Con HID layouts are not the common full-gamepad face/trigger index map.
    public static bool SupportsFullGamepadUnlabeledIndexFallback(string? displayName, ushort hardwareVendorId)
        => GamepadControllerIdentity.IsFullGamepadKnownFamily(displayName, hardwareVendorId);

    // Compatibility alias used by Windows wiring and existing call sites.
    public static bool SupportsFallback(string? displayName, ushort hardwareVendorId)
        => SupportsFullGamepadUnlabeledIndexFallback(displayName, hardwareVendorId);

    /// <summary>
    /// Projects unlabeled full-gamepad HID button indexes using family identity.
    /// Xbox and Nintendo full pads share physical bottom/right/west/north at 0-3.
    /// Sony DualShock/DualSense HID reports Square/Cross/Circle/Triangle at 0-3.
    /// Digital trigger clicks commonly appear at 6/7 across these full pads.
    /// Prefer labeled <see cref="RawGameControllerButtonLabel"/> evidence when present.
    /// </summary>
    public static GamepadInputReading Apply(
        int buttonIndex,
        GamepadInputReading reading,
        string? displayName,
        ushort hardwareVendorId)
    {
        if (GamepadControllerIdentity.IsSony(displayName, hardwareVendorId))
        {
            return ApplySonyHidFaceIndexes(buttonIndex, reading);
        }

        return ApplyPhysicalBottomEastWestNorthFaceIndexes(buttonIndex, reading);
    }

    // Xbox (A B X Y) and Switch Pro physical (B A Y X) both place app semantics at
    // bottom=Activate, east=Back, west=no-op, north=Voice on HID indexes 0-3.
    private static GamepadInputReading ApplyPhysicalBottomEastWestNorthFaceIndexes(
        int buttonIndex,
        GamepadInputReading reading)
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

    // DualSense / DualShock HID descriptor face order is Square, Cross, Circle, Triangle.
    private static GamepadInputReading ApplySonyHidFaceIndexes(
        int buttonIndex,
        GamepadInputReading reading)
        => buttonIndex switch
        {
            0 => reading, // Square (west) — no app action
            1 => reading with { Activate = true }, // Cross (bottom)
            2 => reading with { Back = true }, // Circle (east)
            3 => reading with { ShortcutVoiceToggle = true }, // Triangle (north)
            6 => reading with { LeftTrigger = 1 },
            7 => reading with { RightTrigger = 1 },
            _ => reading
        };
}
