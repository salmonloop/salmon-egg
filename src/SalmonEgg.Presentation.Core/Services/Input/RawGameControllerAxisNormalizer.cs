using System;

namespace SalmonEgg.Presentation.Core.Services.Input;

public static class RawGameControllerAxisNormalizer
{
    private const double CenteredAxisValue = 0.5;
    private const double StandardAxisScale = 2.0;

    public static double NormalizeHorizontal(double value)
        => NormalizeCenteredAxis(value);

    public static double NormalizeVertical(double value)
        => -NormalizeCenteredAxis(value);

    /// <summary>
    /// Maps a unipolar raw HID axis (typical LT/RT report range 0..1 at rest..full) into
    /// unit trigger travel. Non-finite samples are treated as released.
    /// </summary>
    public static double NormalizeUnit(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>
    /// Stick idle samples are exactly zero or non-finite. Centered rest (0.5) is not idle:
    /// it still maps through the bipolar normalizer to thumbstick 0. Trigger slots must not
    /// be consulted here — see <see cref="RawGameControllerInputReadingMapper"/>.
    /// </summary>
    public static bool AreStickAxesIdle(double horizontal, double vertical)
        => IsIdleAxisSample(horizontal) && IsIdleAxisSample(vertical);

    public static bool IsAllAxesZero(IReadOnlyList<double> axes)
    {
        foreach (var axis in axes)
        {
            if (double.IsFinite(axis) && axis != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdleAxisSample(double value)
        => !double.IsFinite(value) || value == 0;

    private static double NormalizeCenteredAxis(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        var normalized = (value - CenteredAxisValue) * StandardAxisScale;
        return Math.Clamp(normalized, -1.0, 1.0);
    }
}
