using System.Collections.Generic;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// Telemetry 配置。
/// 键名与 OpenTelemetry 环境变量规范对齐（OTEL_SERVICE_NAME / OTEL_EXPORTER_OTLP_ENDPOINT 等）。
/// </summary>
public sealed class TelemetrySettings
{
    /// <summary>
    /// 是否启用 Telemetry
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// OTLP 导出端点（如 http://localhost:4318）
    /// </summary>
    public string? OtlpEndpoint { get; init; }

    /// <summary>
    /// OTLP 协议（gRPC 或 HTTP/Protobuf）
    /// </summary>
    public OtlpProtocol Protocol { get; init; } = OtlpProtocol.HttpProtobuf;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; init; } = "SalmonEgg";

    /// <summary>
    /// 服务版本
    /// </summary>
    public string? ServiceVersion { get; init; }

    /// <summary>
    /// 自定义 Resource Attributes
    /// </summary>
    public Dictionary<string, string> ResourceAttributes { get; init; } = new();

    /// <summary>
    /// 采样配置
    /// </summary>
    public SamplingSettings Sampling { get; init; } = SamplingSettings.CreateDesktopDefaults();
}

/// <summary>
/// OTLP 协议类型
/// </summary>
public enum OtlpProtocol
{
    /// <summary>
    /// gRPC 协议（性能最好，但 WASM 不支持）
    /// </summary>
    Grpc,

    /// <summary>
    /// HTTP/Protobuf 协议（兼容性最好）
    /// </summary>
    HttpProtobuf
}
