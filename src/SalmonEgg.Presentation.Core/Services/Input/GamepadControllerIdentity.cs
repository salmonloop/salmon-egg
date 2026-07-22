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

    public static bool IsXbox(string? displayName, ushort hardwareVendorId)
    {
        if (hardwareVendorId == MicrosoftVendorId)
        {
            return true;
        }

        // "Xbox" covers Series / One / 360 product strings. Exclude pure "XInput"
        // generic wrappers without Xbox branding; those are not authoritative family facts.
        return ContainsToken(displayName, "Xbox");
    }

    public static bool IsSony(string? displayName, ushort hardwareVendorId)
    {
        if (hardwareVendorId == SonyVendorId)
        {
            return true;
        }

        // Name tokens for HID paths that omit Sony VID metadata (BT aliases, third-party
        // drivers, browser Gamepad.id short names). Spaced Dual Shock / Dual Sense forms
        // appear on some host strings alongside the compact DualShock / DualSense tokens.
        return ContainsToken(displayName, "PlayStation")
            || ContainsToken(displayName, "DualShock")
            || ContainsToken(displayName, "DualSense")
            || ContainsToken(displayName, "Dual Shock")
            || ContainsToken(displayName, "Dual Sense")
            || ContainsToken(displayName, "PS5")
            || ContainsToken(displayName, "PS4")
            || ContainsToken(displayName, "DS5")
            || ContainsToken(displayName, "DS4");
    }

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

        // Vendor ids alone are sufficient for known full-gamepad families; name tokens
        // cover HID paths that omit vendor metadata (see IsXbox / IsSony / IsNintendo).
        return IsXbox(displayName, hardwareVendorId)
            || IsSony(displayName, hardwareVendorId)
            || IsNintendo(displayName, hardwareVendorId);
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
