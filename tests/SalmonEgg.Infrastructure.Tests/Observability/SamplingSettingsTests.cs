using System;
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
        Assert.Equal(1.0, settings.ErrorRate);
        Assert.Equal(0.1, settings.NormalRate);
        Assert.Equal(0.5, settings.SlowOperationRate);
        Assert.Equal(1.0, settings.VerySlowOperationRate);
        Assert.Equal(0.5, settings.CriticalOperationRate);
        Assert.Equal(3000, settings.SlowOperationThresholdMs);
        Assert.Equal(10000, settings.VerySlowOperationThresholdMs);
    }

    [Fact]
    public void CreateWasmDefaults_HasLowerRatesThanDesktop()
    {
        // Act
        var wasmSettings = SamplingSettings.CreateWasmDefaults();
        var desktopSettings = SamplingSettings.CreateDesktopDefaults();

        // Assert - WASM is more conservative due to network constraints
        Assert.Equal(1.0, wasmSettings.ErrorRate); // Errors still 100%
        Assert.True(wasmSettings.NormalRate < desktopSettings.NormalRate);
        Assert.Equal(0.05, wasmSettings.NormalRate);
        Assert.Equal(0.3, wasmSettings.SlowOperationRate);
        Assert.Equal(0.8, wasmSettings.VerySlowOperationRate);
    }

    [Fact]
    public void CreateMobileDefaults_HasLowestRates()
    {
        // Act
        var mobileSettings = SamplingSettings.CreateMobileDefaults();
        var wasmSettings = SamplingSettings.CreateWasmDefaults();
        var desktopSettings = SamplingSettings.CreateDesktopDefaults();

        // Assert - Mobile is most conservative due to battery/network
        Assert.Equal(1.0, mobileSettings.ErrorRate); // Errors still 100%
        Assert.True(mobileSettings.NormalRate < wasmSettings.NormalRate);
        Assert.True(mobileSettings.NormalRate < desktopSettings.NormalRate);
        Assert.Equal(0.02, mobileSettings.NormalRate);
        Assert.Equal(0.3, mobileSettings.SlowOperationRate);
        Assert.Equal(0.8, mobileSettings.VerySlowOperationRate);
    }

    [Fact]
    public void AllPlatforms_ErrorRateAlways100Percent()
    {
        // Assert
        Assert.Equal(1.0, SamplingSettings.CreateDesktopDefaults().ErrorRate);
        Assert.Equal(1.0, SamplingSettings.CreateWasmDefaults().ErrorRate);
        Assert.Equal(1.0, SamplingSettings.CreateMobileDefaults().ErrorRate);
    }

    [Fact]
    public void AllPlatforms_VerySlowOperationRateIsHighOrMax()
    {
        // Assert - Desktop always 100%, WASM/Mobile at 80% (still high priority)
        Assert.Equal(1.0, SamplingSettings.CreateDesktopDefaults().VerySlowOperationRate);
        Assert.Equal(0.8, SamplingSettings.CreateWasmDefaults().VerySlowOperationRate);
        Assert.Equal(0.8, SamplingSettings.CreateMobileDefaults().VerySlowOperationRate);
    }

    [Fact]
    public void CriticalOperations_AlwaysContainsExpectedNames()
    {
        // Act
        var settings = SamplingSettings.CreateDesktopDefaults();

        // Assert
        Assert.Contains("SessionStart", settings.CriticalOperations);
        Assert.Contains("ChatSubmit", settings.CriticalOperations);
        Assert.Contains("ChatComplete", settings.CriticalOperations);
    }
}
