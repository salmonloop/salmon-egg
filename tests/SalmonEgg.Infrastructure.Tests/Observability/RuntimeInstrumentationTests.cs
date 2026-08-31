using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using SalmonEgg.Infrastructure.Observability;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Observability;

/// <summary>
/// 运行时指标（<c>dotnet.*</c>）的装配门禁。
/// </summary>
/// <remarks>
/// 断言的是「导出器真的收到了 dotnet.* 指标」这一可观察结果，而不是「调用过
/// AddRuntimeInstrumentation」这类实现摆放：后者无法区分「插装接上了」与「接上但收不到
/// 数据」。
///
/// 反向验证记录：移除 <c>TelemetryManager</c> 中的 <c>AddRuntimeInstrumentation()</c>
/// 调用，EmitsDotnetRuntimeMetrics 会因导出集里没有 dotnet.* 而失败；把能力位判断改成
/// 无条件调用，SkipsRuntimeInstrumentation_WhenPlatformDoesNotSupportIt 会失败。
/// </remarks>
public sealed class RuntimeInstrumentationTests
{
    [Fact]
    public void EmitsDotnetRuntimeMetrics_WhenPlatformSupportsIt()
    {
        var exporter = new MetricNameCollectingExporter();
        var factory = new RuntimeCapableExporterFactory(
            runtimeInstrumentationSupported: true,
            exporter);
        var manager = new TelemetryManager(CreateEnabledSettings(), factory);

        try
        {
            manager.Reconfigure(CreateEnabledSettings());

            // 触发若干 GC / 分配，使运行时插装有可观测数值可上报。
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Assert.True(manager.Flush(10000), "metrics flush should complete");

            Assert.Contains(exporter.MetricNames, name => name.StartsWith("dotnet.", StringComparison.Ordinal));
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public void SkipsRuntimeInstrumentation_WhenPlatformDoesNotSupportIt()
    {
        var exporter = new MetricNameCollectingExporter();
        var factory = new RuntimeCapableExporterFactory(
            runtimeInstrumentationSupported: false,
            exporter);
        var manager = new TelemetryManager(CreateEnabledSettings(), factory);

        try
        {
            // WASM 上无条件调用 AddRuntimeInstrumentation 会抛 PlatformNotSupportedException
            // 并让整条管线装配失败，因此这里首先要求 Reconfigure 本身不抛。
            var exception = Record.Exception(() => manager.Reconfigure(CreateEnabledSettings()));
            Assert.Null(exception);

            GC.Collect();
            manager.Flush(10000);

            Assert.DoesNotContain(exporter.MetricNames, name => name.StartsWith("dotnet.", StringComparison.Ordinal));
        }
        finally
        {
            manager.Dispose();
        }
    }

    private static TelemetrySettings CreateEnabledSettings() => new()
    {
        Enabled = true,
        ServiceName = "RuntimeInstrumentationTests",
        OtlpEndpoint = "http://localhost:4318",
        Traces = new OtlpSignalSettings { Endpoint = "http://localhost:4318" },
        Metrics = new OtlpSignalSettings { Endpoint = "http://localhost:4318" },
        Logs = new OtlpSignalSettings { Endpoint = "http://localhost:4318" },
        Sampling = SamplingSettings.CreateDesktopDefaults()
    };

    /// <summary>
    /// 只提供一个记录用的 metric reader，不接真实 OTLP：本门禁验证的是
    /// meter 注册与插装门控，与导出协议无关。
    /// </summary>
    private sealed class RuntimeCapableExporterFactory(
        bool runtimeInstrumentationSupported,
        MetricNameCollectingExporter exporter) : ITelemetryExporterFactory
    {
        public bool IsGrpcSupported => true;

        public bool IsFileSupported => true;

        public bool IsRuntimeInstrumentationSupported { get; } = runtimeInstrumentationSupported;

        public void ConfigureTracerProvider(
            OpenTelemetry.Trace.TracerProviderBuilder builder,
            TelemetrySettings settings)
        {
        }

        public void ConfigureMeterProvider(MeterProviderBuilder builder, TelemetrySettings settings)
        {
            // 手动 reader：ForceFlush 时同步收集，避免依赖周期性导出的时序。
            builder.AddReader(new BaseExportingMetricReader(exporter));
        }

        public void ConfigureLoggerProvider(
            OpenTelemetry.Logs.OpenTelemetryLoggerOptions options,
            TelemetrySettings settings)
        {
        }
    }

    private sealed class MetricNameCollectingExporter : BaseExporter<Metric>
    {
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);
        private readonly Lock _sync = new();

        public IReadOnlyCollection<string> MetricNames
        {
            get
            {
                lock (_sync)
                {
                    return _names.ToList();
                }
            }
        }

        public override ExportResult Export(in Batch<Metric> batch)
        {
            // Batch<T> 是 ref struct，不能在 lock 作用域外枚举，先取出名称再合并。
            var collected = new List<string>();
            foreach (var metric in batch)
            {
                collected.Add(metric.Name);
            }

            lock (_sync)
            {
                foreach (var name in collected)
                {
                    _names.Add(name);
                }
            }

            return ExportResult.Success;
        }
    }
}
