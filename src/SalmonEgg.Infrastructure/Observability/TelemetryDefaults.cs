namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// OpenTelemetry 的默认配置常量（不暴露给用户设置界面）。
///
/// 这些值是开发者决定的合理默认值，用户无需也不应该修改。
/// 高级用户可通过 <see cref="Domain.Models.AppSettings.TelemetryCustomEndpoint"/> 覆盖端点。
/// </summary>
internal static class TelemetryDefaults
{
    /// <summary>
    /// 默认的 OTLP 基础端点（HTTP/Protobuf，应用会按信号附加 /v1/{traces,metrics,logs}）。
    /// </summary>
    /// <remarks>
    /// 客户端刻意不内置任何凭证：把 ingest key 打进客户端等于公开它。因此该默认端点要真正
    /// 可用，必须由服务端代理注入凭证；否则后端会以 401 拒绝每一次导出。
    ///
    /// 2026-08-13 实测：该域名经 Cloudflare 反代回源到 New Relic（GET / 返回 200 且带
    /// nr-rate-limited 响应头），但 POST /v1/traces 被 Cloudflare 以 403 HTML 页拦下，
    /// 未到达 New Relic。判断此类端点是否可用时不能只看 DNS/TLS 是否通——要看 POST 的响应
    /// 是不是 protobuf 形状（对照组：otlp.nr-data.net 无凭证时返回 401 +
    /// content-type: application/x-protobuf）。
    /// </remarks>
    public const string DefaultOtlpEndpoint = "https://otlp.shangxin.me";

    /// <summary>
    /// 服务名称，用于 OpenTelemetry 资源属性（service.name）。
    /// </summary>
    public const string ServiceName = "SalmonEgg";

    /// <summary>
    /// 默认部署环境标识。生产环境应通过环境变量 OTEL_ENVIRONMENT 覆盖。
    /// </summary>
    public const string DefaultEnvironment = "production";

    /// <summary>
    /// Metrics 导出间隔（毫秒）。
    /// </summary>
    public const int MetricsExportIntervalMs = 60_000;

    /// <summary>
    /// Traces 导出间隔（毫秒）。
    /// </summary>
    public const int TracesExportIntervalMs = 5_000;

    /// <summary>
    /// Desktop 平台基础采样率（10% = 开发调试友好）。
    /// </summary>
    public const double DesktopBaseSamplingRate = 0.10;

    /// <summary>
    /// Mobile 平台基础采样率（1% = 省电省流量）。
    /// </summary>
    public const double MobileBaseSamplingRate = 0.01;

    /// <summary>
    /// WebAssembly 平台基础采样率（5% = 防控制台洪泛）。
    /// </summary>
    public const double WasmBaseSamplingRate = 0.05;

    /// <summary>
    /// Desktop 慢 span 采样率（100% = 完整捕获高延迟请求）。
    /// </summary>
    public const double DesktopSlowSpanSamplingRate = 1.0;

    /// <summary>
    /// Mobile 慢 span 采样率（10% = 平衡性能与数据收集）。
    /// </summary>
    public const double MobileSlowSpanSamplingRate = 0.10;

    /// <summary>
    /// WebAssembly 慢 span 采样率（20% = 关注性能瓶颈但不过载）。
    /// </summary>
    public const double WasmSlowSpanSamplingRate = 0.20;

    /// <summary>
    /// 慢 span 判定阈值（毫秒）：超过此延迟的 span 视为慢请求。
    /// </summary>
    public const int SlowSpanThresholdMs = 1000;
}
