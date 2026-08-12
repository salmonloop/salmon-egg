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

        // WASM 只能用 HTTP/Protobuf
        builder.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(settings.OtlpEndpoint);
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
        });
    }

    public void ConfigureMeterProvider(MeterProviderBuilder builder, TelemetrySettings settings)
    {
        builder.AddConsoleExporter();

        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        builder.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(settings.OtlpEndpoint);
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
        });
    }

    public void ConfigureLoggerProvider(OpenTelemetryLoggerOptions loggerOptions, TelemetrySettings settings)
    {
        loggerOptions.AddConsoleExporter();

        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        loggerOptions.AddOtlpExporter(exporterOptions =>
        {
            exporterOptions.Endpoint = new Uri(settings.OtlpEndpoint);
            exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
        });
    }
}
