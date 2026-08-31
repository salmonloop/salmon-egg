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
    /// 平台是否支持 .NET 运行时指标插装（GC / JIT / 线程池 / 工作集）。
    /// </summary>
    /// <remarks>
    /// WASM 必须为 <c>false</c>：<c>AddRuntimeInstrumentation()</c> 依赖 <c>EventSource</c> 与
    /// <c>System.Diagnostics.Process</c>，浏览器沙箱两者都没有，调用会直接抛
    /// <c>PlatformNotSupportedException</c>（open-telemetry/opentelemetry-dotnet-contrib#2529）。
    /// 该异常发生在装配阶段，会让整条遥测管线构造失败，而不只是少掉运行时指标。
    ///
    /// 之所以做成能力位而不是在装配处写 <c>#if __WASM__</c>：平台差异必须集中在平台服务
    /// （AGENTS.md 第 4/9 条），与 <see cref="IsGrpcSupported"/> 同型。
    /// </remarks>
    bool IsRuntimeInstrumentationSupported { get; }

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
