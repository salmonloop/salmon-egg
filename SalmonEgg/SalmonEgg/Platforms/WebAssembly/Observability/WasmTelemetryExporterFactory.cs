using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SalmonEgg.Infrastructure.Observability;

namespace SalmonEgg.Platforms.WebAssembly.Observability;

/// <summary>
/// WASM 平台的 Telemetry 导出器工厂
/// 限制：
/// - 不支持 gRPC（浏览器沙盒限制）
/// - 不支持文件导出
/// - 需要 CORS 配置
/// - 降低导出频率以减少网络请求
/// </summary>
public sealed class WasmTelemetryExporterFactory : ITelemetryExporterFactory
{
    public bool IsGrpcSupported => false;
    public bool IsFileSupported => false;

    // AddRuntimeInstrumentation 依赖 EventSource 与 System.Diagnostics.Process，浏览器沙箱
    // 两者都没有，调用会抛 PlatformNotSupportedException 并使整条管线装配失败
    // （open-telemetry/opentelemetry-dotnet-contrib#2529）。
    public bool IsRuntimeInstrumentationSupported => false;

    public void ConfigureTracerProvider(TracerProviderBuilder builder, TelemetrySettings settings)
    {
        // WASM 总是附加 Console Exporter（输出到浏览器 DevTools Console）
        builder.AddConsoleExporter();

        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        builder.AddOtlpExporter(options => OtlpExporterOptionsConfigurator.Apply(
            options,
            settings,
            OtlpSignal.Traces,
            IsGrpcSupported));
    }

    public void ConfigureMeterProvider(MeterProviderBuilder builder, TelemetrySettings settings)
    {
        builder.AddConsoleExporter();

        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        builder.AddOtlpExporter(options => OtlpExporterOptionsConfigurator.Apply(
            options,
            settings,
            OtlpSignal.Metrics,
            IsGrpcSupported));
    }

    public void ConfigureLoggerProvider(OpenTelemetryLoggerOptions options, TelemetrySettings settings)
    {
        options.AddConsoleExporter();

        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        options.AddOtlpExporter(exporterOptions => OtlpExporterOptionsConfigurator.Apply(
            exporterOptions,
            settings,
            OtlpSignal.Logs,
            IsGrpcSupported));
    }
}
