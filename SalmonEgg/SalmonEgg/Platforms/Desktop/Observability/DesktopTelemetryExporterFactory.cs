using System;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SalmonEgg.Infrastructure.Observability;

namespace SalmonEgg.Platforms.Desktop.Observability;

/// <summary>
/// Desktop 平台的 Telemetry 导出器工厂
/// 支持 gRPC 和 HTTP/Protobuf，性能优先
/// </summary>
public sealed class DesktopTelemetryExporterFactory : ITelemetryExporterFactory
{
    public bool IsGrpcSupported => true;
    public bool IsFileSupported => true;

    public void ConfigureTracerProvider(TracerProviderBuilder builder, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            builder.AddConsoleExporter();
            return;
        }

        builder.AddOtlpExporter(options => ApplyOtlpOptions(options, settings));
    }

    public void ConfigureMeterProvider(MeterProviderBuilder builder, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            builder.AddConsoleExporter();
            return;
        }

        builder.AddOtlpExporter(options => ApplyOtlpOptions(options, settings));
    }

    public void ConfigureLoggerProvider(OpenTelemetryLoggerOptions options, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            options.AddConsoleExporter();
            return;
        }

        options.AddOtlpExporter(exporterOptions => ApplyOtlpOptions(exporterOptions, settings));
    }

    /// <summary>
    /// 三个信号维度共用同一份 endpoint / protocol / headers。
    /// </summary>
    /// <remarks>
    /// 抽出来是为了让"漏配 headers"不可能只发生在某一个维度：认证头一旦只加在其中一路，
    /// 另外两路会被后端以 401 静默拒绝，而应用侧看起来"遥测已开启"。
    /// </remarks>
    private void ApplyOtlpOptions(OtlpExporterOptions options, TelemetrySettings settings)
    {
        options.Endpoint = new Uri(settings.OtlpEndpoint!);
        options.Protocol = settings.Protocol == OtlpProtocol.Grpc && IsGrpcSupported
            ? OtlpExportProtocol.Grpc
            : OtlpExportProtocol.HttpProtobuf;

        // 空字符串会被 SDK 当成"有一个空 header 列表"解析，故仅在真的有值时赋值。
        if (!string.IsNullOrWhiteSpace(settings.OtlpHeaders))
        {
            options.Headers = settings.OtlpHeaders;
        }
    }
}
