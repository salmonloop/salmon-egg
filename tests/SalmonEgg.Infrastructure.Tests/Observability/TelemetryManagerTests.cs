using System;
using SalmonEgg.Infrastructure.Observability;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Observability;

public class TelemetryManagerTests
{
    [Fact]
    public void Initialize_WithDisabledTelemetry_DoesNotThrow()
    {
        // Arrange
        var settings = new TelemetrySettings
        {
            Enabled = false,
            ServiceName = "Test"
        };
        var factory = new TestTelemetryExporterFactory();
        var manager = new TelemetryManager(settings, factory);

        // Act & Assert
        var exception = Record.Exception(() => manager.Initialize());
        Assert.Null(exception);
    }

    [Fact]
    public void Initialize_WithEnabledTelemetry_CreatesProviders()
    {
        // Arrange
        var settings = new TelemetrySettings
        {
            Enabled = true,
            ServiceName = "Test",
            OtlpEndpoint = "http://localhost:4318",
            Sampling = SamplingSettings.CreateDesktopDefaults()
        };
        var factory = new TestTelemetryExporterFactory();
        var manager = new TelemetryManager(settings, factory);

        // Act
        manager.Initialize();

        // Assert
        Assert.True(factory.TracerProviderConfigured);
        Assert.True(factory.MeterProviderConfigured);
        Assert.True(manager.IsEnabled);
    }

    [Fact]
    public void Shutdown_WithUninitializedManager_DoesNotThrow()
    {
        // Arrange
        var settings = new TelemetrySettings { Enabled = false, ServiceName = "Test" };
        var factory = new TestTelemetryExporterFactory();
        var manager = new TelemetryManager(settings, factory);

        // Act & Assert
        var exception = Record.Exception(() => manager.Shutdown());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_DisposesProviders()
    {
        // Arrange
        var settings = new TelemetrySettings
        {
            Enabled = true,
            ServiceName = "Test",
            OtlpEndpoint = "http://localhost:4318",
            Sampling = SamplingSettings.CreateDesktopDefaults()
        };
        var factory = new TestTelemetryExporterFactory();
        var manager = new TelemetryManager(settings, factory);
        manager.Initialize();

        // Act
        manager.Dispose();

        // Assert - should not throw when disposed multiple times
        var exception = Record.Exception(() => manager.Dispose());
        Assert.Null(exception);
    }

    private sealed class TestTelemetryExporterFactory : ITelemetryExporterFactory
    {
        public bool IsGrpcSupported => true;
        public bool IsFileSupported => true;
        public bool TracerProviderConfigured { get; private set; }
        public bool MeterProviderConfigured { get; private set; }

        public void ConfigureTracerProvider(
            OpenTelemetry.Trace.TracerProviderBuilder builder,
            TelemetrySettings settings)
        {
            TracerProviderConfigured = true;
        }

        public void ConfigureMeterProvider(
            OpenTelemetry.Metrics.MeterProviderBuilder builder,
            TelemetrySettings settings)
        {
            MeterProviderConfigured = true;
        }

        public void ConfigureLoggerProvider(
            OpenTelemetry.Logs.OpenTelemetryLoggerOptions options,
            TelemetrySettings settings)
        {
            // No-op for testing
        }
    }
}
