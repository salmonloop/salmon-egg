using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// Telemetry 生命周期管理接口，统一管理 Traces / Metrics / Logs 三个维度。
///
/// 方法为同步：底层 OpenTelemetry .NET 的 <c>Shutdown</c> / <c>ForceFlush</c> 本身是
/// 同步阻塞 API（内部按 timeout 等待导出完成），不存在异步重载。此处不包装成
/// <c>Task</c> 以免制造“看似可等待、实则同步阻塞”的假异步。
/// </summary>
public interface ITelemetryManager
{
    /// <summary>
    /// 初始化 OpenTelemetry（应用启动时调用）。重复调用是幂等的。
    /// </summary>
    void Initialize();

    /// <summary>
    /// 关闭 OpenTelemetry（应用退出时调用），在 timeout 内尽量导出残留数据。
    /// </summary>
    /// <param name="timeoutMilliseconds">等待上限；-1 表示无限等待。</param>
    /// <returns>是否在超时前完成关闭。</returns>
    bool Shutdown(int timeoutMilliseconds = 5000);

    /// <summary>
    /// 强制刷新所有缓冲数据。
    /// </summary>
    /// <param name="timeoutMilliseconds">等待上限；-1 表示无限等待。</param>
    /// <returns>是否在超时前完成刷新。</returns>
    bool Flush(int timeoutMilliseconds = 5000);

    /// <summary>
    /// 当前 TracerProvider；未启用或初始化失败时为 null。
    /// </summary>
    TracerProvider? TracerProvider { get; }

    /// <summary>
    /// 当前 MeterProvider；未启用或初始化失败时为 null。
    /// </summary>
    MeterProvider? MeterProvider { get; }

    /// <summary>
    /// Telemetry 是否已启用且初始化成功。
    /// </summary>
    bool IsEnabled { get; }
}
