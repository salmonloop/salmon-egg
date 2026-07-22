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
        var normalized = NormalizeProfileId(profileId);

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

        // Confirmed Xbox catalog ids and any future unlisted Microsoft HIDMaestro
        // profiles default to Xbox. Non-Xbox families must be listed explicitly above.
        return GamepadControllerFamily.Xbox;
    }

    /// <summary>
    /// Invariant family token used by bridge <c>info</c> and Diagnostics captures.
    /// </summary>
    public static string FormatFamilyToken(string? profileId)
        => ResolveFamily(profileId) switch
        {
            GamepadControllerFamily.Sony => "Sony",
            GamepadControllerFamily.Nintendo => "Nintendo",
            GamepadControllerFamily.Xbox => "Xbox",
            _ => "Unknown"
        };
}
