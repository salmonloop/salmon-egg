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
///
/// 导出器契约与 Desktop / Windows 一致：配置了 OTLP 端点 → 只走 OTLP；
/// 未配置 → console 兜底。此前 WASM 是无条件双导出（console + OTLP 同时挂），
/// 与其他平台不一致且生产流量被拖慢。
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
        // 与 Desktop / Windows 同一契约：配置了端点只走 OTLP；未配置时 console 兜底
        // （输出到浏览器 DevTools Console），避免生产流量被双份导出拖慢。
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            builder.AddConsoleExporter();
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
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            builder.AddConsoleExporter();
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
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            options.AddConsoleExporter();
            return;
        }

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
