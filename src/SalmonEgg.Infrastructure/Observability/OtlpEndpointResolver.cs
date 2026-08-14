using System;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// Resolves the final OTLP endpoint for a signal.
/// </summary>
/// <remarks>
/// The OpenTelemetry .NET exporter appends <c>/v1/{signal}</c> only when its endpoint was not
/// explicitly configured. SalmonEgg always configures the endpoint after merging user settings,
/// environment variables, and defaults, so HTTP/Protobuf paths must be resolved here.
/// </remarks>
public static class OtlpEndpointResolver
{
    private const string TracesPath = "/v1/traces";
    private const string MetricsPath = "/v1/metrics";
    private const string LogsPath = "/v1/logs";

    public static Uri Resolve(string endpoint, OtlpProtocol protocol, OtlpSignal signal)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("An OTLP endpoint is required.", nameof(endpoint));
        }

        var uri = new Uri(endpoint, UriKind.Absolute);
        if (protocol == OtlpProtocol.Grpc)
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        var basePath = TrimKnownSignalPath(builder.Path);
        builder.Path = basePath + GetSignalPath(signal);
        return builder.Uri;
    }

    private static string TrimKnownSignalPath(string path)
    {
        var normalizedPath = path.TrimEnd('/');

        foreach (var signalPath in new[] { TracesPath, MetricsPath, LogsPath })
        {
            if (normalizedPath.EndsWith(signalPath, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedPath[..^signalPath.Length].TrimEnd('/');
            }
        }

        return normalizedPath;
    }

    private static string GetSignalPath(OtlpSignal signal) => signal switch
    {
        OtlpSignal.Traces => TracesPath,
        OtlpSignal.Metrics => MetricsPath,
        OtlpSignal.Logs => LogsPath,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
    };
}

public enum OtlpSignal
{
    Traces,
    Metrics,
    Logs
}
