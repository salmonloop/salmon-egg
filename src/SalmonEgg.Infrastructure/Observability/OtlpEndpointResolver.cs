using System;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// Resolves a final OTLP exporter endpoint. OTLP/HTTP signal-specific endpoints are final and are
/// therefore used verbatim; only generic HTTP/protobuf base endpoints receive a signal path.
/// </summary>
public static class OtlpEndpointResolver
{
    private const string TracesPath = "/v1/traces";
    private const string MetricsPath = "/v1/metrics";
    private const string LogsPath = "/v1/logs";

    public static Uri Resolve(string endpoint, OtlpProtocol protocol, OtlpSignal signal)
        => Resolve(OtlpSignalSettings.Create(endpoint, null, protocol, false), signal);

    public static Uri Resolve(OtlpSignalSettings settings, OtlpSignal signal)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            throw new ArgumentException("An OTLP endpoint is required.", nameof(settings));
        }

        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("The OTLP endpoint must be an absolute HTTP(S) URI without user info, query, or fragment.", nameof(settings));
        }

        if (settings.Protocol == OtlpProtocol.Grpc || settings.IsSignalSpecificEndpoint)
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
