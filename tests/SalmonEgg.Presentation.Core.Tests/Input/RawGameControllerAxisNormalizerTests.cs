using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class RawGameControllerAxisNormalizerTests
{
    [Theory]
    [InlineData(0.0, -1.0)]
    [InlineData(0.25, -0.5)]
    [InlineData(0.5, 0.0)]
    [InlineData(0.75, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(-0.25, -1.0)]
    [InlineData(1.25, 1.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    public void NormalizeHorizontal_MapsRawAxisRangeToStandardThumbstickRange(double rawValue, double expected)
    {
        var normalized = RawGameControllerAxisNormalizer.NormalizeHorizontal(rawValue);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.25, 0.5)]
    [InlineData(0.5, 0.0)]
    [InlineData(0.75, -0.5)]
    [InlineData(1.0, -1.0)]
    [InlineData(-0.25, 1.0)]
    [InlineData(1.25, -1.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    public void NormalizeVertical_InvertsRawAxisRangeForStandardThumbstickY(double rawValue, double expected)
    {
        var normalized = RawGameControllerAxisNormalizer.NormalizeVertical(rawValue);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void IsAllAxesZero_ReturnsTrueOnlyWhenEveryAxisIsExactlyZero()
    {
        Assert.True(RawGameControllerAxisNormalizer.IsAllAxesZero([0.0, 0.0, 0.0]));
        Assert.False(RawGameControllerAxisNormalizer.IsAllAxesZero([0.0, 0.5, 0.0]));
    }

    [Fact]
    public void IsAllAxesZero_TreatsNonFiniteValuesAsIdle()
    {
        Assert.True(RawGameControllerAxisNormalizer.IsAllAxesZero([0.0, double.NaN, double.NegativeInfinity]));
        Assert.False(RawGameControllerAxisNormalizer.IsAllAxesZero([0.0, double.PositiveInfinity, 0.5]));
    }
}
