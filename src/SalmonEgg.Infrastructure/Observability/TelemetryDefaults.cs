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
    /// No collector is configured by default. Telemetry only starts after the user or deployment
    /// supplies a validated OTLP endpoint.
    /// </summary>
    public const string? DefaultOtlpEndpoint = null;

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
