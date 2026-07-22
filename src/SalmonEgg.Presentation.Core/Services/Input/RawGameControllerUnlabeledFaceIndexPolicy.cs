namespace SalmonEgg.Presentation.Core.Services.Input;

public static class RawGameControllerUnlabeledFaceIndexPolicy
{
    private const ushort MicrosoftVendorId = 0x045E;
    private const ushort SonyVendorId = 0x054C;
    private const ushort NintendoVendorId = 0x057E;

    public static bool SupportsFallback(string? displayName, ushort hardwareVendorId)
    {
        if (hardwareVendorId is MicrosoftVendorId or SonyVendorId or NintendoVendorId)
        {
            return true;
        }

        return ContainsToken(displayName, "Xbox")
            || ContainsToken(displayName, "PlayStation")
            || ContainsToken(displayName, "DualShock")
            || ContainsToken(displayName, "DualSense")
            || ContainsToken(displayName, "Nintendo")
            || ContainsToken(displayName, "Switch Pro")
            || ContainsToken(displayName, "Joy-Con")
            || ContainsToken(displayName, "JoyCon");
    }

    // Common HID face order for Xbox (A B X Y), DualShock/DualSense (Cross Circle Square Triangle),
    // and Switch Pro (B A Y X) is physical bottom/right/west/north. That projects to the same app
    // face semantics used by the standard Gamepad path.
    public static GamepadInputReading Apply(int buttonIndex, GamepadInputReading reading)
        => buttonIndex switch
        {
            0 => reading with { Activate = true },
            1 => reading with { Back = true },
            2 => reading,
            3 => reading with { ShortcutVoiceToggle = true },
            _ => reading
        };

    private static bool ContainsToken(string? value, string token)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains(token, StringComparison.OrdinalIgnoreCase);
    }
}
