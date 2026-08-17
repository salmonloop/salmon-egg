using SalmonEgg.Infrastructure.Observability;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Observability;

public class SamplingSettingsTests
{
    [Fact]
    public void CreateDesktopDefaults_HasCorrectRates()
    {
        // Act
        var settings = SamplingSettings.CreateDesktopDefaults();

        // Assert
        Assert.Equal(0.1, settings.NormalRate);
    }

    [Fact]
    public void CreateWasmDefaults_HasLowerRatesThanDesktop()
    {
        // Act
        var wasmSettings = SamplingSettings.CreateWasmDefaults();
        var desktopSettings = SamplingSettings.CreateDesktopDefaults();

        // Assert - WASM is more conservative due to network constraints
        Assert.True(wasmSettings.NormalRate < desktopSettings.NormalRate);
        Assert.Equal(0.05, wasmSettings.NormalRate);
    }

    [Fact]
    public void CreateMobileDefaults_HasLowestRates()
    {
        // Act
        var mobileSettings = SamplingSettings.CreateMobileDefaults();
        var wasmSettings = SamplingSettings.CreateWasmDefaults();
        var desktopSettings = SamplingSettings.CreateDesktopDefaults();

        // Assert - Mobile is most conservative due to battery/network
        Assert.True(mobileSettings.NormalRate < wasmSettings.NormalRate);
        Assert.True(mobileSettings.NormalRate < desktopSettings.NormalRate);
        Assert.Equal(0.02, mobileSettings.NormalRate);
    }
}
