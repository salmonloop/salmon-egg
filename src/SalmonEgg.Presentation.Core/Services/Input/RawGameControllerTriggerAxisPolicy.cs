namespace SalmonEgg.Presentation.Core.Services.Input;

/// <summary>
/// Projects analog trigger travel from raw HID axis slots for known full-gamepad
/// families that expose unipolar LT/RT after the two sticks. Platform services pass
/// axis facts only; this Core owner decides when indices 4/5 are triggers.
/// Nintendo full pads keep digital L2/R2 (buttons/labels); they do not use this map.
/// Prefer labeled or unlabeled digital trigger evidence when already present: axis
/// values merge with <c>Math.Max</c> so partial travel and full digital clicks coexist.
/// </summary>
public static class RawGameControllerTriggerAxisPolicy
{
    public const int LeftTriggerAxisIndex = 4;
    public const int RightTriggerAxisIndex = 5;
    public const int MinimumAxisCountForAnalogTriggers = 6;

    public static bool SupportsAnalogTriggerAxes(string? displayName, ushort hardwareVendorId)
    {
        var family = GamepadControllerIdentity.ResolveFamily(displayName, hardwareVendorId);
        return family is GamepadControllerFamily.Xbox or GamepadControllerFamily.Sony;
    }

    public static GamepadInputReading Apply(
        IReadOnlyList<double> axes,
        GamepadInputReading reading,
        string? displayName,
        ushort hardwareVendorId)
    {
        ArgumentNullException.ThrowIfNull(axes);

        if (!SupportsAnalogTriggerAxes(displayName, hardwareVendorId)
            || axes.Count < MinimumAxisCountForAnalogTriggers)
        {
            return reading;
        }

        var leftFromAxes = RawGameControllerAxisNormalizer.NormalizeUnit(axes[LeftTriggerAxisIndex]);
        var rightFromAxes = RawGameControllerAxisNormalizer.NormalizeUnit(axes[RightTriggerAxisIndex]);
        if (leftFromAxes <= 0 && rightFromAxes <= 0)
        {
            return reading;
        }

        return reading with
        {
            LeftTrigger = Math.Max(reading.LeftTrigger, leftFromAxes),
            RightTrigger = Math.Max(reading.RightTrigger, rightFromAxes)
        };
    }
}
