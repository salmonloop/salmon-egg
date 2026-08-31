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


    public static GamepadControllerFamily ResolveFamily(string? displayName, ushort hardwareVendorId)
    {
        // Order is VID/name family helpers; known full controllers are mutually exclusive.
        if (IsXbox(displayName, hardwareVendorId))
        {
            return GamepadControllerFamily.Xbox;
        }

        if (IsSony(displayName, hardwareVendorId))
        {
            return GamepadControllerFamily.Sony;
        }

        if (IsNintendo(displayName, hardwareVendorId))
        {
            return GamepadControllerFamily.Nintendo;
        }

        return GamepadControllerFamily.Unknown;
    }

    public static GamepadControllerFamily ResolveFamily(string? displayName, ushort? hardwareVendorId)
        => ResolveFamily(displayName, hardwareVendorId ?? 0);



    /// <summary>
    /// Invariant family token for Diagnostics captures and multi-brand validation notes.
    /// Not localized UI chrome — keep tokens stable: Xbox / Sony / Nintendo / Unknown.
    /// </summary>
    public static string FormatFamilyToken(GamepadControllerFamily family)
        => family switch
        {
            GamepadControllerFamily.Xbox => "Xbox",
            GamepadControllerFamily.Sony => "Sony",
            GamepadControllerFamily.Nintendo => "Nintendo",
            _ => "Unknown"
        };

    public static GamepadControllerFamily ResolveFamily(
        string? displayName,
        ushort? hardwareVendorId,
        StandardGamepadFaceButtonLabels faceButtonLabels)
    {
        var fromIdentity = ResolveFamily(displayName, hardwareVendorId);
        if (fromIdentity != GamepadControllerFamily.Unknown)
        {
            return fromIdentity;
        }

        return ResolveFamilyFromFaceButtonLabels(faceButtonLabels);
    }

    public static GamepadControllerFamily ResolveFamilyFromFaceButtonLabels(
        StandardGamepadFaceButtonLabels faceButtonLabels)
        => ResolveFamilyFromLabels(
            faceButtonLabels.A,
            faceButtonLabels.B,
            faceButtonLabels.X,
            faceButtonLabels.Y);

    public static GamepadControllerFamily ResolveFamilyFromLabels(
        params RawGameControllerButtonLabel[] labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var sawXbox = false;
        var sawSony = false;
        var sawNintendo = false;
        foreach (var label in labels)
        {
            switch (label)
            {
                case RawGameControllerButtonLabel.XboxA:
                case RawGameControllerButtonLabel.XboxB:
                case RawGameControllerButtonLabel.XboxX:
                case RawGameControllerButtonLabel.XboxY:
                    sawXbox = true;
                    break;
                case RawGameControllerButtonLabel.Cross:
                case RawGameControllerButtonLabel.Circle:
                case RawGameControllerButtonLabel.Square:
                case RawGameControllerButtonLabel.Triangle:
                    sawSony = true;
                    break;
                case RawGameControllerButtonLabel.LetterA:
                case RawGameControllerButtonLabel.LetterB:
                case RawGameControllerButtonLabel.LetterX:
                case RawGameControllerButtonLabel.LetterY:
                    sawNintendo = true;
                    break;
            }
        }

        // Mutual exclusion: prefer glyph families over Xbox when mixed (should not happen).
        if (sawSony && !sawNintendo && !sawXbox)
        {
            return GamepadControllerFamily.Sony;
        }

        if (sawNintendo && !sawSony && !sawXbox)
        {
            return GamepadControllerFamily.Nintendo;
        }

        if (sawXbox && !sawSony && !sawNintendo)
        {
            return GamepadControllerFamily.Xbox;
        }

        return GamepadControllerFamily.Unknown;
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
