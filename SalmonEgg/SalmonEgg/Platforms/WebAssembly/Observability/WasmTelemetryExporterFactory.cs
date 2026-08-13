using System;
using OpenTelemetry.Exporter;
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

    public void ConfigureTracerProvider(TracerProviderBuilder builder, TelemetrySettings settings)
    {
        // WASM 总是附加 Console Exporter（输出到浏览器 DevTools Console）
        builder.AddConsoleExporter();

        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        builder.AddOtlpExporter(options => ApplyOtlpOptions(options, settings));
    }

    public void ConfigureMeterProvider(MeterProviderBuilder builder, TelemetrySettings settings)
    {
        builder.AddConsoleExporter();

        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        builder.AddOtlpExporter(options => ApplyOtlpOptions(options, settings));
    }

    public void ConfigureLoggerProvider(OpenTelemetryLoggerOptions options, TelemetrySettings settings)
    {
        options.AddConsoleExporter();

        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        options.AddOtlpExporter(exporterOptions => ApplyOtlpOptions(exporterOptions, settings));
    }

    /// <summary>
    /// 三个信号维度共用同一份 endpoint / headers；WASM 强制 HTTP/Protobuf（浏览器无 gRPC）。
    /// </summary>
    /// <remarks>
    /// 抽出来是为了让"漏配 headers"不可能只发生在某一个维度：认证头一旦只加在其中一路，
    /// 另外两路会被后端以 401 静默拒绝，而应用侧看起来"遥测已开启"。
    /// </remarks>
    private static void ApplyOtlpOptions(OtlpExporterOptions options, TelemetrySettings settings)
    {
        options.Endpoint = new Uri(settings.OtlpEndpoint!);
        options.Protocol = OtlpExportProtocol.HttpProtobuf;

        if (!string.IsNullOrWhiteSpace(settings.OtlpHeaders))
        {
            options.Headers = settings.OtlpHeaders;
        }
    }
}
