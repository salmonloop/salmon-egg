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

}
