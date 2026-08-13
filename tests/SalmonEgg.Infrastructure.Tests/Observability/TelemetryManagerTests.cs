using System;
using System.Collections.Generic;
using SalmonEgg.Infrastructure.Observability;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Observability;

public class TelemetryManagerTests
{
    [Fact]
    public void Reconfigure_WithDisabledTelemetry_DoesNotThrow()
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
        var exception = Record.Exception(() => manager.Reconfigure(settings));
        Assert.Null(exception);
    }

    [Fact]
    public void Reconfigure_WithEnabledTelemetry_CreatesProviders()
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
        manager.Reconfigure(settings);

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
        manager.Reconfigure(settings);

        // Act
        manager.Dispose();

        // Assert - should not throw when disposed multiple times
        var exception = Record.Exception(() => manager.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Reconfigure_FromInactiveBootstrap_BuildsProviders()
    {
        // 容器以"未激活"配置构造 manager，真实配置随后 apply——这就是启动装配的唯一形态。
        var factory = new TestTelemetryExporterFactory();
        var manager = new TelemetryManager(TelemetrySettings.CreateInactiveBootstrap(), factory);
        Assert.False(manager.IsEnabled);

        manager.Reconfigure(CreateEnabledSettings("http://localhost:4318"));

        Assert.True(manager.IsEnabled);
        Assert.True(factory.TracerProviderConfigured);
        Assert.True(factory.MeterProviderConfigured);
    }

    [Fact]
    public void Reconfigure_ToDisabled_TearsDownWithoutRebuilding()
    {
        var factory = new TestTelemetryExporterFactory();
        var manager = new TelemetryManager(TelemetrySettings.CreateInactiveBootstrap(), factory);
        manager.Reconfigure(CreateEnabledSettings("http://localhost:4318"));

        manager.Reconfigure(new TelemetrySettings { Enabled = false, ServiceName = "Test" });

        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public void Reconfigure_AfterDispose_IsIgnored()
    {
        // 关闭过程中若还有一次保存落盘，重建必须被忽略而不是复活一套 provider。
        var factory = new TestTelemetryExporterFactory();
        var manager = new TelemetryManager(TelemetrySettings.CreateInactiveBootstrap(), factory);
        manager.Dispose();

        manager.Reconfigure(CreateEnabledSettings("http://localhost:4318"));

        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public void Reconfigure_SwitchesLogsAlongWithTracesAndMetrics()
    {
        // 只切 traces/metrics 而把日志留在旧端点，是这类实现最容易漏的一维；
        // 直接测 DynamicTelemetryLoggerProvider 覆盖不到 manager 到它之间的这条接线。
        var factory = new TestTelemetryExporterFactory();
        using var loggerProvider = new DynamicTelemetryLoggerProvider(factory);
        var manager = new TelemetryManager(
            TelemetrySettings.CreateInactiveBootstrap(),
            factory,
            loggerProvider);

        manager.Reconfigure(CreateEnabledSettings("http://first.example.com:4318"));
        manager.Reconfigure(CreateEnabledSettings("http://second.example.com:4318"));

        Assert.Equal(
            new[] { "http://first.example.com:4318", "http://second.example.com:4318" },
            factory.LoggerEndpoints);
    }

    [Fact]
    public void Reconfigure_ToDisabled_StopsConfiguringLogs()
    {
        // 用户关掉开关后不得再为 Logs 维度装配导出器，否则日志仍会外发。
        var factory = new TestTelemetryExporterFactory();
        using var loggerProvider = new DynamicTelemetryLoggerProvider(factory);
        var manager = new TelemetryManager(
            TelemetrySettings.CreateInactiveBootstrap(),
            factory,
            loggerProvider);
        manager.Reconfigure(CreateEnabledSettings("http://first.example.com:4318"));

        manager.Reconfigure(new TelemetrySettings { Enabled = false, ServiceName = "Test" });

        Assert.Single(factory.LoggerEndpoints);
    }

    private static TelemetrySettings CreateEnabledSettings(string endpoint) => new()
    {
        Enabled = true,
        ServiceName = "Test",
        OtlpEndpoint = endpoint,
        Sampling = SamplingSettings.CreateDesktopDefaults()
    };

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

        /// <summary>每次装配 Logs 维度时收到的端点，用于证明日志与 traces 一起切换。</summary>
        public List<string?> LoggerEndpoints { get; } = new();

        public void ConfigureLoggerProvider(
            OpenTelemetry.Logs.OpenTelemetryLoggerOptions options,
            TelemetrySettings settings)
        {
            LoggerEndpoints.Add(settings.OtlpEndpoint);
        }
    }
}
