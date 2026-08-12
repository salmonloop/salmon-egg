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

        // WinUI3 与 Desktop 相同，优先 gRPC
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

    public void ConfigureLoggerProvider(OpenTelemetryLoggerOptions loggerOptions, TelemetrySettings settings)
    {
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            loggerOptions.AddConsoleExporter();
            return;
        }

        loggerOptions.AddOtlpExporter(exporterOptions =>
        {
            exporterOptions.Endpoint = new Uri(settings.OtlpEndpoint);
            exporterOptions.Protocol = settings.Protocol == OtlpProtocol.Grpc && IsGrpcSupported
                ? OtlpExportProtocol.Grpc
                : OtlpExportProtocol.HttpProtobuf;
        });
    }
}
