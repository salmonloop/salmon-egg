using System;
using OpenTelemetry.Exporter;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// Applies SalmonEgg's effective OTLP transport configuration to one signal exporter.
/// </summary>
/// <remarks>
/// Endpoint path resolution, protocol fallback, and headers are one contract. Keeping them here
/// prevents platform factories from configuring only part of that contract for one signal.
/// </remarks>
public static class OtlpExporterOptionsConfigurator
{
    public static void Apply(
        OtlpExporterOptions options,
        TelemetrySettings settings,
        OtlpSignal signal,
        bool grpcSupported)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        var protocol = settings.Protocol == OtlpProtocol.Grpc && grpcSupported
            ? OtlpExportProtocol.Grpc
            : OtlpExportProtocol.HttpProtobuf;

        options.Protocol = protocol;
        options.Endpoint = OtlpEndpointResolver.Resolve(
            settings.OtlpEndpoint!,
            protocol == OtlpExportProtocol.Grpc ? OtlpProtocol.Grpc : OtlpProtocol.HttpProtobuf,
            signal);

        // An empty value is not equivalent to no headers: the SDK attempts to parse it as a
        // configured header list. Assign only a real OTLP key=value list.
        if (!string.IsNullOrWhiteSpace(settings.OtlpHeaders))
        {
            options.Headers = settings.OtlpHeaders;
        }
    }
}
