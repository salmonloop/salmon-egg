using System;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SalmonEgg.Infrastructure.Observability;

namespace SalmonEgg.Platforms.Windows.Observability;

/// <summary>
/// WinUI3 平台的 Telemetry 导出器工厂
/// 与 Desktop 类似，但可选集成 ETW (Event Tracing for Windows)
/// </summary>
public sealed class WinUI3TelemetryExporterFactory : ITelemetryExporterFactory
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

        if (!string.IsNullOrWhiteSpace(settings.OtlpHeaders))
        {
            options.Headers = settings.OtlpHeaders;
        }
    }
}
