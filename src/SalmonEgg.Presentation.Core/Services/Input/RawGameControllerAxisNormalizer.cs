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
