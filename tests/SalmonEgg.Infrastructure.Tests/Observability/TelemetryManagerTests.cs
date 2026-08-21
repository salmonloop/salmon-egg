using System;
using System.Collections.Generic;
using System.Diagnostics;
using SalmonEgg.Acp.Observability;
using SalmonEgg.Application.Observability;
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
            Traces = new OtlpSignalSettings { Endpoint = "http://localhost:4318" },
            Metrics = new OtlpSignalSettings { Endpoint = "http://localhost:4318" },
            Logs = new OtlpSignalSettings { Endpoint = "http://localhost:4318" },
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
            Traces = new OtlpSignalSettings { Endpoint = "http://localhost:4318" },
            Metrics = new OtlpSignalSettings { Endpoint = "http://localhost:4318" },
            Logs = new OtlpSignalSettings { Endpoint = "http://localhost:4318" },
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

    [Fact]
    public void Reconfigure_ToDisabledThenBackToEnabled_RebuildsAllSignals()
    {
        // 关闭会拆掉 provider；重新打开必须能在拆除后的状态上完整重建三路信号，
        // 否则用户"关了再开"会得到一个静默失效的遥测管线。
        var factory = new TestTelemetryExporterFactory();
        using var loggerProvider = new DynamicTelemetryLoggerProvider(factory);
        var manager = new TelemetryManager(
            TelemetrySettings.CreateInactiveBootstrap(),
            factory,
            loggerProvider);
        manager.Reconfigure(CreateEnabledSettings("http://first.example.com:4318"));
        manager.Reconfigure(new TelemetrySettings { Enabled = false, ServiceName = "Test" });
        Assert.False(manager.IsEnabled);
        Assert.Null(manager.TracerProvider);
        Assert.Null(manager.MeterProvider);

        manager.Reconfigure(CreateEnabledSettings("http://second.example.com:4318"));

        Assert.True(manager.IsEnabled);
        Assert.NotNull(manager.TracerProvider);
        Assert.NotNull(manager.MeterProvider);
        Assert.Equal(2, factory.TracerConfigureCount);
        Assert.Equal(2, factory.MeterConfigureCount);
        Assert.Equal(
            new[] { "http://first.example.com:4318", "http://second.example.com:4318" },
            factory.LoggerEndpoints);
    }

    [Fact]
    public void Reconfigure_WhenReplacementBuildFails_KeepsCurrentPipelineAndAllowsRetry()
    {
        var factory = new TestTelemetryExporterFactory();
        var manager = new TelemetryManager(TelemetrySettings.CreateInactiveBootstrap(), factory);
        manager.Reconfigure(CreateEnabledSettings("http://first.example.com:4318"));
        var originalTracer = manager.TracerProvider;
        var originalMeter = manager.MeterProvider;
        factory.ThrowOnMeterConfiguration = true;
        var replacement = CreateEnabledSettings("http://second.example.com:4318");

        Assert.Throws<InvalidOperationException>(() => manager.Reconfigure(replacement));

        Assert.True(manager.IsEnabled);
        Assert.Same(originalTracer, manager.TracerProvider);
        Assert.Same(originalMeter, manager.MeterProvider);

        factory.ThrowOnMeterConfiguration = false;
        manager.Reconfigure(replacement);

        Assert.True(manager.IsEnabled);
        Assert.NotSame(originalTracer, manager.TracerProvider);
        Assert.NotSame(originalMeter, manager.MeterProvider);
    }

    [Fact]
    public void Reconfigure_WhenLoggerReplacementBuildFails_KeepsCurrentPipelineAndAllowsRetry()
    {
        var factory = new TestTelemetryExporterFactory();
        using var loggerProvider = new DynamicTelemetryLoggerProvider(factory);
        using var manager = new TelemetryManager(
            TelemetrySettings.CreateInactiveBootstrap(),
            factory,
            loggerProvider);
        manager.Reconfigure(CreateEnabledSettings("http://first.example.com:4318"));
        var originalTracer = manager.TracerProvider;
        var originalMeter = manager.MeterProvider;
        factory.ThrowOnLoggerConfiguration = true;

        Assert.Throws<InvalidOperationException>(() =>
            manager.Reconfigure(CreateEnabledSettings("http://second.example.com:4318")));

        Assert.True(manager.IsEnabled);
        Assert.Same(originalTracer, manager.TracerProvider);
        Assert.Same(originalMeter, manager.MeterProvider);
        Assert.Equal(new[] { "http://first.example.com:4318" }, factory.LoggerEndpoints);

        factory.ThrowOnLoggerConfiguration = false;
        manager.Reconfigure(CreateEnabledSettings("http://second.example.com:4318"));

        Assert.True(manager.IsEnabled);
        Assert.Equal(
            new[] { "http://first.example.com:4318", "http://second.example.com:4318" },
            factory.LoggerEndpoints);
    }

    [Fact]
    public void Reconfigure_UsesConfiguredNormalSamplingRate()
    {
        var factory = new TestTelemetryExporterFactory();
        using var manager = new TelemetryManager(TelemetrySettings.CreateInactiveBootstrap(), factory);
        manager.Reconfigure(CreateEnabledSettings("http://localhost:4318", normalRate: 0));

        using (var dropped = ApplicationActivitySources.ChatService.StartActivity("sampling-drop"))
        {
            Assert.NotNull(dropped);
            Assert.False(dropped!.Recorded);
        }

        manager.Reconfigure(CreateEnabledSettings("http://localhost:4318", normalRate: 1));

        using var sampled = ApplicationActivitySources.ChatService.StartActivity("sampling-record");
        Assert.NotNull(sampled);
        Assert.True(sampled!.Recorded);

        using var acpSource = new ActivitySource(AcpActivitySources.ClientName);
        using var acpSampled = acpSource.StartActivity("acp-sampling-record");
        Assert.NotNull(acpSampled);
        Assert.True(acpSampled!.Recorded);
    }

    private static TelemetrySettings CreateEnabledSettings(string endpoint, double normalRate = 0.1) => new()
    {
        Enabled = true,
        ServiceName = "Test",
        OtlpEndpoint = endpoint,
        Sampling = new SamplingSettings { NormalRate = normalRate }
    };

    private sealed class TestTelemetryExporterFactory : ITelemetryExporterFactory
    {
        public bool IsGrpcSupported => true;
        public bool IsFileSupported => true;
        public bool TracerProviderConfigured { get; private set; }
        public bool MeterProviderConfigured { get; private set; }
        public int TracerConfigureCount { get; private set; }
        public int MeterConfigureCount { get; private set; }

        public bool ThrowOnMeterConfiguration { get; set; }

        public bool ThrowOnLoggerConfiguration { get; set; }

        public void ConfigureTracerProvider(
            OpenTelemetry.Trace.TracerProviderBuilder builder,
            TelemetrySettings settings)
        {
            TracerProviderConfigured = true;
            TracerConfigureCount++;
        }

        public void ConfigureMeterProvider(
            OpenTelemetry.Metrics.MeterProviderBuilder builder,
            TelemetrySettings settings)
        {
            if (ThrowOnMeterConfiguration)
            {
                throw new InvalidOperationException("meter exporter configuration failed");
            }

            MeterProviderConfigured = true;
            MeterConfigureCount++;
        }

        /// <summary>每次装配 Logs 维度时收到的端点，用于证明日志与 traces 一起切换。</summary>
        public List<string?> LoggerEndpoints { get; } = new();

        public void ConfigureLoggerProvider(
            OpenTelemetry.Logs.OpenTelemetryLoggerOptions options,
            TelemetrySettings settings)
        {
            if (ThrowOnLoggerConfiguration)
            {
                throw new InvalidOperationException("logger exporter configuration failed");
            }

            LoggerEndpoints.Add(settings.OtlpEndpoint);
        }
    }
}
