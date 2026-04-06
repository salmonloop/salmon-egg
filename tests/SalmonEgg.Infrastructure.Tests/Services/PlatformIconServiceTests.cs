using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class PlatformIconServiceTests
{
    [Theory]
    [InlineData(true, IconUsage.Interactive, true, true)]
    [InlineData(true, IconUsage.Static, true, false)]
    [InlineData(false, IconUsage.Interactive, false, false)]
    [InlineData(false, IconUsage.Static, false, false)]
    public void GetPresentation_ReturnsExpectedPlatformCapabilities(
        bool isWindows,
        IconUsage usage,
        bool expectedPrefersNativeIcon,
        bool expectedSupportsNativeAnimation)
    {
        var service = new PlatformIconService(isWindows);

        var presentation = service.GetPresentation(usage);

        Assert.Equal(expectedPrefersNativeIcon, presentation.PrefersNativeIcon);
        Assert.Equal(expectedSupportsNativeAnimation, presentation.SupportsNativeAnimation);
    }

    [Fact]
    public void GetPresentation_DefaultConstructor_ReturnsDeterministicResultForUsage()
    {
        var service = new PlatformIconService();

        var interactive = service.GetPresentation(IconUsage.Interactive);
        var @static = service.GetPresentation(IconUsage.Static);

        Assert.False(@static.SupportsNativeAnimation);
        Assert.True(interactive.PrefersNativeIcon || !interactive.SupportsNativeAnimation);
        Assert.True(@static.PrefersNativeIcon || !@static.SupportsNativeAnimation);
    }
}
