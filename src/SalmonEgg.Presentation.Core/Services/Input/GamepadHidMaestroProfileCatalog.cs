namespace SalmonEgg.Presentation.Core.Services.Input;

/// <summary>
/// Confirmed HIDMaestro virtual-controller profile ids used by native-device
/// OS-path smoke. Single Core owner for profile → family mapping so the Windows
/// bridge and diagnostics evidence stay aligned. Do not invent profile ids here;
/// only add ids after confirming they exist in an installed HIDMaestro package.
/// </summary>
public static class GamepadHidMaestroProfileCatalog
{
    public const string DefaultProfileId = "xbox-360-wired";

    public const string Xbox360Wired = "xbox-360-wired";
    public const string XboxSeriesXs = "xbox-series-xs";
    public const string DualSense = "dualsense";
    public const string DualSenseBluetooth = "dualsense-bt";
    public const string DualShock4V2 = "dualshock-4-v2";
    public const string SwitchPro = "switch-pro";

    public static IReadOnlyList<string> ConfirmedProfileIds { get; } =
    [
        Xbox360Wired,
        XboxSeriesXs,
        DualSense,
        DualSenseBluetooth,
        DualShock4V2,
        SwitchPro
    ];

    public static bool IsConfirmedProfileId(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        foreach (var confirmed in ConfirmedProfileIds)
        {
            if (string.Equals(confirmed, profileId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string NormalizeProfileId(string? profileId)
        => string.IsNullOrWhiteSpace(profileId)
            ? DefaultProfileId
            : profileId.Trim();

    public static GamepadControllerFamily ResolveFamily(string? profileId)
    {
        // Blank/missing config means "use the default confirmed Xbox profile" for
        // bridge startup. That is not the same as inventing family for a free-form id.
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return GamepadControllerFamily.Xbox;
        }

        var normalized = profileId.Trim();
        if (!IsConfirmedProfileId(normalized))
        {
            // Unconfirmed HIDMaestro ids must not default to Xbox identity evidence.
            return GamepadControllerFamily.Unknown;
        }

        if (string.Equals(normalized, SwitchPro, StringComparison.OrdinalIgnoreCase))
        {
            return GamepadControllerFamily.Nintendo;
        }

        if (string.Equals(normalized, DualSense, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, DualSenseBluetooth, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, DualShock4V2, StringComparison.OrdinalIgnoreCase))
        {
            return GamepadControllerFamily.Sony;
        }

        // Remaining confirmed catalog ids are Xbox family.
        return GamepadControllerFamily.Xbox;
    }

    /// <summary>
    /// Invariant family token used by bridge <c>info</c> and Diagnostics captures.
    /// </summary>
    public static string FormatFamilyToken(string? profileId)
        => GamepadControllerIdentity.FormatFamilyToken(ResolveFamily(profileId));

    /// <summary>
    /// Ordered physical HMButton field-name candidates for an app face semantic.
    /// Confirmed Sony profiles prefer PS glyph keys with Xbox-letter fallbacks;
    /// Nintendo uses physical letter positions; Xbox and unconfirmed profiles use
    /// Xbox-letter inject keys only (unconfirmed still reports family Unknown via
    /// <see cref="FormatFamilyToken"/> so Diagnostics evidence is not claimed).
    /// </summary>
    public static IReadOnlyList<string> GetPhysicalButtonNameCandidates(
        string? profileId,
        GamepadFaceSemantic semantic)
    {
        return ResolveFamily(profileId) switch
        {
            GamepadControllerFamily.Nintendo => semantic switch
            {
                // Physical Switch Pro face: B bottom / A east / Y west / X north.
                GamepadFaceSemantic.Activate => ["B"],
                GamepadFaceSemantic.Back => ["A"],
                GamepadFaceSemantic.West => ["Y"],
                GamepadFaceSemantic.Voice => ["X"],
                _ => throw new ArgumentOutOfRangeException(nameof(semantic), semantic, null)
            },
            GamepadControllerFamily.Sony => semantic switch
            {
                // DualSense / DualShock: prefer PS glyph keys, fall back to A/B/X/Y
                // when a HIDMaestro build only exposes Xbox-style field names.
                GamepadFaceSemantic.Activate => ["Cross", "A"],
                GamepadFaceSemantic.Back => ["Circle", "B"],
                GamepadFaceSemantic.West => ["Square", "X"],
                GamepadFaceSemantic.Voice => ["Triangle", "Y"],
                _ => throw new ArgumentOutOfRangeException(nameof(semantic), semantic, null)
            },
            // Xbox (confirmed) and Unknown inject fallback share A/B/X/Y field names.
            _ => semantic switch
            {
                GamepadFaceSemantic.Activate => ["A"],
                GamepadFaceSemantic.Back => ["B"],
                GamepadFaceSemantic.West => ["X"],
                GamepadFaceSemantic.Voice => ["Y"],
                _ => throw new ArgumentOutOfRangeException(nameof(semantic), semantic, null)
            }
        };
    }
}
