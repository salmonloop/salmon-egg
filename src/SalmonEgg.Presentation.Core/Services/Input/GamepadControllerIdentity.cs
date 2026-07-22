namespace SalmonEgg.Presentation.Core.Services.Input;

/// <summary>
/// Single Core owner for gamepad family identity tokens used by layout resolution
/// and unlabeled full-gamepad index fallback. Platform services only pass facts
/// (display name, vendor id); they must not re-encode brand rules.
/// </summary>
public static class GamepadControllerIdentity
{
    public const ushort MicrosoftVendorId = 0x045E;
    public const ushort SonyVendorId = 0x054C;
    public const ushort NintendoVendorId = 0x057E;

    public static bool IsNintendo(string? displayName, ushort hardwareVendorId)
    {
        if (hardwareVendorId == NintendoVendorId)
        {
            return true;
        }

        return ContainsToken(displayName, "Nintendo")
            || ContainsToken(displayName, "Switch Pro")
            || IsProControllerName(displayName)
            || ContainsToken(displayName, "Joy-Con")
            || ContainsToken(displayName, "JoyCon");
    }

    public static bool IsFullGamepadKnownFamily(string? displayName, ushort hardwareVendorId)
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
        // PS4/PS5 short names appear on some Windows HID paths without Sony VID metadata.
        return ContainsToken(displayName, "Xbox")
            || ContainsToken(displayName, "PlayStation")
            || ContainsToken(displayName, "DualShock")
            || ContainsToken(displayName, "DualSense")
            || ContainsToken(displayName, "PS5")
            || ContainsToken(displayName, "PS4")
            || ContainsToken(displayName, "Nintendo")
            || ContainsToken(displayName, "Switch Pro")
            || IsProControllerName(displayName)
            || ContainsToken(displayName, "Joy-Con")
            || ContainsToken(displayName, "JoyCon");
    }

    public static bool IsSingleJoyCon(string? displayName)
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

    // "Pro Controller" is the common Switch Pro HID product name. Exclude Xbox-named
    // devices so "Xbox ... Pro ..." strings do not promote Nintendo face layout.
    private static bool IsProControllerName(string? displayName)
        => ContainsToken(displayName, "Pro Controller")
            && !ContainsToken(displayName, "Xbox");

    private static bool ContainsToken(string? value, string token)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains(token, StringComparison.OrdinalIgnoreCase);
    }
}
