using System;
using System.Collections.Generic;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// Immutable, effective OpenTelemetry configuration. OTLP transport settings are resolved per
/// signal so the OpenTelemetry generic and signal-specific environment variables retain their
/// documented precedence and endpoint semantics.
/// </summary>
public sealed class TelemetrySettings
{
    private static readonly string ProcessInstanceId = Guid.NewGuid().ToString("D");

    public bool Enabled { get; init; }

    public string? OtlpEndpoint { get; init; }

    public OtlpProtocol Protocol { get; init; } = OtlpProtocol.HttpProtobuf;

    public string? OtlpHeaders { get; init; }

    public OtlpSignalSettings Traces { get; init; } = OtlpSignalSettings.Inactive;

    public OtlpSignalSettings Metrics { get; init; } = OtlpSignalSettings.Inactive;

    public OtlpSignalSettings Logs { get; init; } = OtlpSignalSettings.Inactive;

    public string ServiceName { get; init; } = TelemetryDefaults.ServiceName;

    public string? ServiceVersion { get; init; }

    public Dictionary<string, string> ResourceAttributes { get; init; } = new();

    public SamplingSettings Sampling { get; init; } = new();

    public OtlpSignalSettings GetSignalSettings(OtlpSignal signal)
    {
        var configured = signal switch
        {
            OtlpSignal.Traces => Traces,
            OtlpSignal.Metrics => Metrics,
            OtlpSignal.Logs => Logs,
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
        };

        // Preserve compatibility for callers constructing the legacy flat settings object. The
        // resolver path uses per-signal settings produced by Build, so this fallback is test/host
        // adapter behavior only.
        return configured.IsConfigured || (OtlpEndpoint is null && OtlpHeaders is null)
            ? configured
            : OtlpSignalSettings.Create(OtlpEndpoint, OtlpHeaders, Protocol, false);
    }

    public bool IsEquivalentTo(TelemetrySettings? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (!Enabled || !other.Enabled)
        {
            return Enabled == other.Enabled;
        }

        var leftSignalsConfigured = Traces.IsConfigured || Metrics.IsConfigured || Logs.IsConfigured;
        var rightSignalsConfigured = other.Traces.IsConfigured || other.Metrics.IsConfigured || other.Logs.IsConfigured;
        if (!leftSignalsConfigured && !rightSignalsConfigured)
        {
            return string.Equals(OtlpEndpoint, other.OtlpEndpoint, StringComparison.Ordinal)
                && Protocol == other.Protocol
                && string.Equals(OtlpHeaders, other.OtlpHeaders, StringComparison.Ordinal)
                && string.Equals(ServiceName, other.ServiceName, StringComparison.Ordinal)
                && string.Equals(ServiceVersion, other.ServiceVersion, StringComparison.Ordinal)
                && Sampling.IsEquivalentTo(other.Sampling)
                && AttributesEqual(ResourceAttributes, other.ResourceAttributes);
        }

        return Traces.IsEquivalentTo(other.Traces)
            && Metrics.IsEquivalentTo(other.Metrics)
            && Logs.IsEquivalentTo(other.Logs)
            && string.Equals(ServiceName, other.ServiceName, StringComparison.Ordinal)
            && string.Equals(ServiceVersion, other.ServiceVersion, StringComparison.Ordinal)
            && Sampling.IsEquivalentTo(other.Sampling)
            && AttributesEqual(ResourceAttributes, other.ResourceAttributes);
    }

    private static bool AttributesEqual(Dictionary<string, string> left, Dictionary<string, string> right)
    {
        if (left.Count != right.Count) return false;
        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    public static TelemetrySettings CreateInactiveBootstrap() => new()
    {
        Enabled = false,
        ServiceName = TelemetryDefaults.ServiceName
    };

    /// <summary>
    /// Resolves user intent and OTLP environment variables. A user supplied endpoint is a product
    /// override for all signals; otherwise OTLP's per-signal variables override generic values.
    /// An endpoint is required: the application intentionally has no default external collector.
    /// </summary>
    public static TelemetrySettings Build(
        Domain.Models.AppSettings? userSettings,
        SamplingSettings platformSamplingDefaults,
        string? serviceVersion = null)
    {
        ArgumentNullException.ThrowIfNull(platformSamplingDefaults);

        var userDisabled = userSettings?.TelemetrySharingEnabled == false;
        var sdkDisabled = IsTrue(Environment.GetEnvironmentVariable("OTEL_SDK_DISABLED"));
        var userEndpoint = NormalizeOptional(userSettings?.TelemetryCustomEndpoint);
        var userHeaders = NormalizeOptional(userSettings?.TelemetryAuthHeader);

        var genericEndpoint = NormalizeOptional(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
        var genericHeaders = NormalizeOptional(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS"));
        var genericProtocol = ParseProtocol(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL"));

        var traces = ResolveSignal(OtlpSignal.Traces, userEndpoint, userHeaders, genericEndpoint, genericHeaders, genericProtocol);
        var metrics = ResolveSignal(OtlpSignal.Metrics, userEndpoint, userHeaders, genericEndpoint, genericHeaders, genericProtocol);
        var logs = ResolveSignal(OtlpSignal.Logs, userEndpoint, userHeaders, genericEndpoint, genericHeaders, genericProtocol);
        var enabled = !userDisabled && !sdkDisabled && traces.IsConfigured && metrics.IsConfigured && logs.IsConfigured;

        return new TelemetrySettings
        {
            Enabled = enabled,
            OtlpEndpoint = traces.Endpoint,
            Protocol = traces.Protocol,
            OtlpHeaders = traces.Headers,
            Traces = traces,
            Metrics = metrics,
            Logs = logs,
            ServiceName = NormalizeOptional(Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")) ?? TelemetryDefaults.ServiceName,
            ServiceVersion = serviceVersion,
            ResourceAttributes = new Dictionary<string, string>
            {
                [SemanticConventions.Resource.DeploymentEnvironmentName] =
                    NormalizeOptional(Environment.GetEnvironmentVariable("OTEL_ENVIRONMENT")) ?? TelemetryDefaults.DefaultEnvironment,
                [SemanticConventions.Resource.ServiceInstanceId] = ProcessInstanceId
            },
            Sampling = platformSamplingDefaults
        };
    }

    private static OtlpSignalSettings ResolveSignal(
        OtlpSignal signal,
        string? userEndpoint,
        string? userHeaders,
        string? genericEndpoint,
        string? genericHeaders,
        OtlpProtocol genericProtocol)
    {
        if (userEndpoint is not null)
        {
            return OtlpSignalSettings.Create(userEndpoint, userHeaders, OtlpProtocol.HttpProtobuf, isSignalSpecificEndpoint: false);
        }

        var suffix = signal switch
        {
            OtlpSignal.Traces => "TRACES",
            OtlpSignal.Metrics => "METRICS",
            OtlpSignal.Logs => "LOGS",
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
        };
        var endpoint = NormalizeOptional(Environment.GetEnvironmentVariable($"OTEL_EXPORTER_OTLP_{suffix}_ENDPOINT"));
        var headers = NormalizeOptional(Environment.GetEnvironmentVariable($"OTEL_EXPORTER_OTLP_{suffix}_HEADERS")) ?? genericHeaders;
        var protocol = ParseProtocol(Environment.GetEnvironmentVariable($"OTEL_EXPORTER_OTLP_{suffix}_PROTOCOL"), genericProtocol);
        return OtlpSignalSettings.Create(endpoint ?? genericEndpoint, headers, protocol, endpoint is not null);
    }

    private static OtlpProtocol ParseProtocol(string? value, OtlpProtocol fallback = OtlpProtocol.HttpProtobuf)
        => value?.Trim().ToLowerInvariant() switch
        {
            null or "" => fallback,
            "grpc" => OtlpProtocol.Grpc,
            "http/protobuf" => OtlpProtocol.HttpProtobuf,
            _ => fallback
        };

    private static bool IsTrue(string? value) => string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class OtlpSignalSettings
{
    public static OtlpSignalSettings Inactive { get; } = new();

    public string? Endpoint { get; init; }

    public string? Headers { get; init; }

    public OtlpProtocol Protocol { get; init; } = OtlpProtocol.HttpProtobuf;

    /// <summary>True when OTLP's signal-specific endpoint variable supplied the final endpoint.</summary>
    public bool IsSignalSpecificEndpoint { get; init; }

    public bool IsConfigured => Endpoint is not null;

    internal static OtlpSignalSettings Create(string? endpoint, string? headers, OtlpProtocol protocol, bool isSignalSpecificEndpoint)
        => new()
        {
            Endpoint = endpoint,
            Headers = headers,
            Protocol = protocol,
            IsSignalSpecificEndpoint = isSignalSpecificEndpoint
        };

    internal bool IsEquivalentTo(OtlpSignalSettings other)
        => string.Equals(Endpoint, other.Endpoint, StringComparison.Ordinal)
            && string.Equals(Headers, other.Headers, StringComparison.Ordinal)
            && Protocol == other.Protocol
            && IsSignalSpecificEndpoint == other.IsSignalSpecificEndpoint;
}

public enum OtlpProtocol
{
    Grpc,
    HttpProtobuf
}
