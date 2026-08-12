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

        // Desktop 平台优先使用 gRPC
        builder.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(settings.OtlpEndpoint);
            options.Protocol = settings.Protocol == OtlpProtocol.Grpc && IsGrpcSupported
                ? OtlpExportProtocol.Grpc
                : OtlpExportProtocol.HttpProtobuf;
        });
    }

    public void ConfigureMeterProvider(MeterProviderBuilder builder, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            builder.AddConsoleExporter();
            return;
        }

        builder.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(settings.OtlpEndpoint);
            options.Protocol = settings.Protocol == OtlpProtocol.Grpc && IsGrpcSupported
                ? OtlpExportProtocol.Grpc
                : OtlpExportProtocol.HttpProtobuf;
        });
    }

    public void ConfigureLoggerProvider(OpenTelemetryLoggerOptions options, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            options.AddConsoleExporter();
            return;
        }

        options.AddOtlpExporter(exporterOptions =>
        {
            exporterOptions.Endpoint = new Uri(settings.OtlpEndpoint);
            exporterOptions.Protocol = settings.Protocol == OtlpProtocol.Grpc && IsGrpcSupported
                ? OtlpExportProtocol.Grpc
                : OtlpExportProtocol.HttpProtobuf;
        });
    }
}
