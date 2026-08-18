namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// OpenTelemetry 的应用级默认资源值。
/// </summary>
internal static class TelemetryDefaults
{
    /// <summary>
    /// 服务名称，用于 OpenTelemetry 资源属性（service.name）。
    /// </summary>
    public const string ServiceName = "SalmonEgg";

    /// <summary>
    /// 默认部署环境标识。生产环境应通过环境变量 OTEL_ENVIRONMENT 覆盖。
    /// </summary>
    public const string DefaultEnvironment = "production";

    /// <summary>
    /// 兜底 OTLP 网关。网关在边缘向上游注入鉴权，客户端不内置上游凭证；用户自定义端点、
    /// 分信号与通用 OTEL_EXPORTER_OTLP_*_ENDPOINT 仍可逐级覆盖。HTTP/Protobuf 域名末尾不带
    /// /v1/{signal}，由 OTLP exporter 按 signal 自动追加。
    /// </summary>
    public const string DefaultOtlpEndpoint = "https://otlp.shangxin.me";

}
