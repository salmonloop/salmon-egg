namespace SalmonEgg.Presentation.Core.Services.Input;

public static class RawGameControllerUnlabeledFaceIndexPolicy
{
    private const ushort MicrosoftVendorId = 0x045E;
    private const ushort SonyVendorId = 0x054C;
    private const ushort NintendoVendorId = 0x057E;

    // Single Joy-Con HID layouts are not the common full-gamepad face/trigger index map.
    public static bool SupportsFullGamepadUnlabeledIndexFallback(string? displayName, ushort hardwareVendorId)
    {
        if (IsSingleJoyCon(displayName))
        {
            return false;
        }

        if (hardwareVendorId is MicrosoftVendorId or SonyVendorId or NintendoVendorId)
        {
            return true;
        }

        // Joy-Con name tokens only reach here after single Joy-Con exclusion above.
        return ContainsToken(displayName, "Xbox")
            || ContainsToken(displayName, "PlayStation")
            || ContainsToken(displayName, "DualShock")
            || ContainsToken(displayName, "DualSense")
            || ContainsToken(displayName, "Nintendo")
            || ContainsToken(displayName, "Switch Pro")
            || ContainsToken(displayName, "Joy-Con")
            || ContainsToken(displayName, "JoyCon");
    }

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

    private static bool IsSingleJoyCon(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        var hasJoyCon = ContainsToken(displayName, "Joy-Con") || ContainsToken(displayName, "JoyCon");
        if (!hasJoyCon)
        {
            return false;
        }

        // Pair / grip / dual presentations can still use full-gamepad HID ordering.
        if (ContainsToken(displayName, "Pair")
            || ContainsToken(displayName, "Grip")
            || ContainsToken(displayName, "Dual"))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsToken(string? value, string token)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains(token, StringComparison.OrdinalIgnoreCase);
    }
}
