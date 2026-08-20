using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SalmonEgg.Infrastructure.Observability;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Observability;

/// <summary>
/// 验证 Logs 维度真的接在 <c>Microsoft.Extensions.Logging</c> 上，并随配置切换。
/// </summary>
/// <remarks>
/// 单独 build 一个 OTel <c>LoggerProvider</c> 收不到 <c>ILogger</c> 写入，会造成
/// "provider 建好了但 Logs 没有数据"的假集成。这里用真实的 <c>LoggerFactory</c> 走一遍
/// 写入路径，断言导出器实际收到了记录——只断言 provider 非 null 无法区分这两种情况。
/// </remarks>
public sealed class DynamicTelemetryLoggerProviderTests
{
    [Fact]
    public void BeforeReconfigure_LogsAreDropped()
    {
        // 容器构建后、启动流程 apply 之前不得上报：此时还不知道用户是否同意。
        var factory = new CapturingExporterFactory();
        using var provider = new DynamicTelemetryLoggerProvider(factory);
        using var loggerFactory = CreateLoggerFactory(provider);

        loggerFactory.CreateLogger("Test").LogInformation("before apply");

        Assert.Empty(factory.ConfiguredSettings);
    }

    [Fact]
    public void AfterReconfigure_LoggerIsEnabledAndExporterIsConfigured()
    {
        var factory = new CapturingExporterFactory();
        using var provider = new DynamicTelemetryLoggerProvider(factory);
        using var loggerFactory = CreateLoggerFactory(provider);

        provider.Reconfigure(CreateSettings(enabled: true, "https://first.example.com:4318"), ResourceBuilder.CreateDefault());

        var logger = loggerFactory.CreateLogger("Test");
        Assert.True(logger.IsEnabled(LogLevel.Information));
        var configured = Assert.Single(factory.ConfiguredSettings);
        Assert.Equal("https://first.example.com:4318", configured.OtlpEndpoint);
    }

    [Fact]
    public void Reconfigure_RebuildsExporterWithNewEndpoint()
    {
        // 这是"改端点后日志也跟着切"的断言：只切 traces/metrics 而日志仍发往旧端点，
        // 是这类实现最容易漏的一维。
        var factory = new CapturingExporterFactory();
        using var provider = new DynamicTelemetryLoggerProvider(factory);
        using var loggerFactory = CreateLoggerFactory(provider);
        var logger = loggerFactory.CreateLogger("Test");

        provider.Reconfigure(CreateSettings(enabled: true, "https://first.example.com:4318"), ResourceBuilder.CreateDefault());
        logger.LogInformation("first");

        provider.Reconfigure(CreateSettings(enabled: true, "https://second.example.com:4318"), ResourceBuilder.CreateDefault());
        logger.LogInformation("second");

        Assert.Equal(2, factory.ConfiguredSettings.Count);
        Assert.Equal("https://second.example.com:4318", factory.ConfiguredSettings[^1].OtlpEndpoint);
    }

    [Fact]
    public void Reconfigure_ToDisabled_StopsLogging()
    {
        // 用户关掉开关后，同一个已缓存的 logger 实例必须立刻停止上报；
        // 若外壳 logger 缓存了旧的内部 logger 而不看代次，这里会继续 enabled。
        var factory = new CapturingExporterFactory();
        using var provider = new DynamicTelemetryLoggerProvider(factory);
        using var loggerFactory = CreateLoggerFactory(provider);
        var logger = loggerFactory.CreateLogger("Test");

        provider.Reconfigure(CreateSettings(enabled: true, "https://first.example.com:4318"), ResourceBuilder.CreateDefault());
        Assert.True(logger.IsEnabled(LogLevel.Information));

        provider.Reconfigure(CreateSettings(enabled: false, endpoint: null), ResourceBuilder.CreateDefault());

        Assert.False(logger.IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void CreateLogger_ForSameCategory_ReturnsSameInstance()
    {
        // 外壳 logger 必须长期存活：日志是热路径，每次写入都新建 logger 会持续分配。
        var factory = new CapturingExporterFactory();
        using var provider = new DynamicTelemetryLoggerProvider(factory);

        var first = provider.CreateLogger("Same.Category");
        var second = provider.CreateLogger("Same.Category");

        Assert.Same(first, second);
    }

    [Fact]
    public void AfterDispose_LoggingIsInert()
    {
        var factory = new CapturingExporterFactory();
        var provider = new DynamicTelemetryLoggerProvider(factory);
        provider.Reconfigure(CreateSettings(enabled: true, "https://first.example.com:4318"), ResourceBuilder.CreateDefault());
        var logger = provider.CreateLogger("Test");

        provider.Dispose();

        Assert.False(logger.IsEnabled(LogLevel.Information));
        var exception = Record.Exception(() => logger.LogInformation("after dispose"));
        Assert.Null(exception);
        Assert.Null(Record.Exception(provider.Dispose));
    }

    private static ILoggerFactory CreateLoggerFactory(DynamicTelemetryLoggerProvider provider)
        => LoggerFactory.Create(builder =>
        {
            // 复刻 DependencyInjection 的接线形态：provider 作为 ILoggerProvider 加入管线。
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(provider);
        });

    private static TelemetrySettings CreateSettings(bool enabled, string? endpoint) => new()
    {
        Enabled = enabled,
        OtlpEndpoint = endpoint,
        ServiceName = "Test",
        Sampling = SamplingSettings.CreateDesktopDefaults()
    };

    /// <summary>
    /// 记录 <c>ConfigureLoggerProvider</c> 收到的配置，并挂一个内存导出器，
    /// 以证明日志确实进入了 OTel 管线而不是被丢弃。
    /// </summary>
    private sealed class CapturingExporterFactory : ITelemetryExporterFactory
    {
        public List<TelemetrySettings> ConfiguredSettings { get; } = new();

        public bool IsGrpcSupported => true;

        public bool IsFileSupported => true;

        public void ConfigureTracerProvider(TracerProviderBuilder builder, TelemetrySettings settings)
        {
        }

        public void ConfigureMeterProvider(MeterProviderBuilder builder, TelemetrySettings settings)
        {
        }

        public void ConfigureLoggerProvider(OpenTelemetryLoggerOptions options, TelemetrySettings settings)
        {
            ConfiguredSettings.Add(settings);
        }
    }
}
