using System;
using System.Diagnostics;
using OpenTelemetry.Trace;
using SalmonEgg.Infrastructure.Observability;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Observability;

public class DifferentialSamplerTests
{
    [Fact]
    public void ShouldSample_ParentIsRecorded_ReturnsRecordAndSample()
    {
        // Arrange
        var settings = SamplingSettings.CreateDesktopDefaults();
        var sampler = new DifferentialSampler(settings);
        var parentContext = new ActivityContext(
            ActivityTraceId.CreateRandom(),
            ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);

        var samplingParams = new SamplingParameters(
            parentContext,
            ActivityTraceId.CreateRandom(),
            "TestOperation",
            ActivityKind.Internal,
            null,
            null);

        // Act
        var result = sampler.ShouldSample(samplingParams);

        // Assert
        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
    }

    [Fact]
    public void ShouldSample_NormalOperation_SamplesBasedOnRate()
    {
        // Arrange
        var settings = SamplingSettings.CreateDesktopDefaults();
        var sampler = new DifferentialSampler(settings);

        int recordAndSampleCount = 0;
        int recordOnlyCount = 0;
        const int iterations = 10000;

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var samplingParams = new SamplingParameters(
                default,
                ActivityTraceId.CreateRandom(),
                "NormalOperation",
                ActivityKind.Internal,
                null,
                null);

            var result = sampler.ShouldSample(samplingParams);

            if (result.Decision == SamplingDecision.RecordAndSample)
            {
                recordAndSampleCount++;
            }
            else if (result.Decision == SamplingDecision.RecordOnly)
            {
                recordOnlyCount++;
            }
        }

        // Assert - NormalRate = 0.1, expect around 10% RecordAndSample
        double actualRate = recordAndSampleCount / (double)iterations;
        Assert.InRange(actualRate, 0.08, 0.12);

        // The rest should be RecordOnly (not Drop)
        Assert.True(recordOnlyCount > 0);
        Assert.Equal(iterations, recordAndSampleCount + recordOnlyCount);
    }

    [Fact]
    public void ShouldSample_CriticalOperation_HigherSamplingRate()
    {
        // Arrange
        var settings = SamplingSettings.CreateDesktopDefaults();
        var sampler = new DifferentialSampler(settings);

        int sampledCount = 0;
        const int iterations = 1000;

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var samplingParams = new SamplingParameters(
                default,
                ActivityTraceId.CreateRandom(),
                "SessionStart", // Critical operation
                ActivityKind.Internal,
                null,
                null);

            var result = sampler.ShouldSample(samplingParams);
            if (result.Decision == SamplingDecision.RecordAndSample)
            {
                sampledCount++;
            }
        }

        // Assert - CriticalOperationRate = 0.5, expect around 50%
        double actualRate = sampledCount / (double)iterations;
        Assert.InRange(actualRate, 0.4, 0.6);
    }

    [Fact]
    public void WasmDefaults_HasLowerNormalRate()
    {
        // Arrange & Act
        var wasmSettings = SamplingSettings.CreateWasmDefaults();
        var desktopSettings = SamplingSettings.CreateDesktopDefaults();

        // Assert
        Assert.True(wasmSettings.NormalRate < desktopSettings.NormalRate);
        Assert.Equal(0.05, wasmSettings.NormalRate);
    }

    [Fact]
    public void MobileDefaults_HasLowestNormalRate()
    {
        // Arrange & Act
        var mobileSettings = SamplingSettings.CreateMobileDefaults();

        // Assert
        Assert.Equal(0.02, mobileSettings.NormalRate);
    }
}
