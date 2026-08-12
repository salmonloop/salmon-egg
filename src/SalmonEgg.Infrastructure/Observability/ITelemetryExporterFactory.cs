using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// 平台特定的 Telemetry 导出器工厂接口
/// 不同平台（Desktop/WASM/WinUI3/Mobile）实现此接口提供平台特定的导出器配置
/// </summary>
public interface ITelemetryExporterFactory
{
    /// <summary>
    /// 平台是否支持 gRPC 协议（WASM 为 false）
    /// </summary>
    bool IsGrpcSupported { get; }

    /// <summary>
    /// 平台是否支持文件导出（WASM 为 false）
    /// </summary>
    bool IsFileSupported { get; }

    /// <summary>
    /// 配置 TracerProvider（Traces 维度）
    /// </summary>
    /// <param name="builder">TracerProviderBuilder 实例</param>
    /// <param name="settings">Telemetry 配置</param>
    void ConfigureTracerProvider(TracerProviderBuilder builder, TelemetrySettings settings);

    /// <summary>
    /// 配置 MeterProvider（Metrics 维度）
    /// </summary>
    /// <param name="builder">MeterProviderBuilder 实例</param>
    /// <param name="settings">Telemetry 配置</param>
    void ConfigureMeterProvider(MeterProviderBuilder builder, TelemetrySettings settings);

    /// <summary>
    /// 配置 LoggerProvider（Logs 维度）
    /// </summary>
    /// <param name="builder">OpenTelemetryLoggerOptions 实例</param>
    /// <param name="settings">Telemetry 配置</param>
    void ConfigureLoggerProvider(OpenTelemetryLoggerOptions builder, TelemetrySettings settings);
}
