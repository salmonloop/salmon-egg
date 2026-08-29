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
    /// 进程退出路径使用的 <see cref="Shutdown"/> 超时：不等待导出完成。
    /// </summary>
    /// <remarks>
    /// 0 不是"跳过关闭"，而是 OpenTelemetry SDK 显式支持的非阻塞分支：
    /// <c>BatchExportProcessor.OnShutdown</c> 与 <c>BatchExportThreadWorker.Shutdown</c>
    /// 都对 0 单独取分支——置位 drain target、唤醒导出线程，但不 Join。语义是
    /// "通知导出线程去收尾，但调用方不等它"。
    ///
    /// 关闭路径必须用它：<see cref="Shutdown"/> 逐个 provider 串行等待，tracer 与 meter
    /// 各等一遍默认 5000ms，端点不可达时实测把关闭拖到 10s 以上（issue #126）。
    /// 进程退出手感优先于最后一批 span 的送达率。
    /// </remarks>
    public const int NonBlockingShutdownTimeoutMilliseconds = 0;

    /// <summary>
    /// 按给定配置装配遥测管线，使端点 / 凭证 / 开关变更立即生效，无需重启。
    /// 启动时的首次装配也走这里。
    /// </summary>
    /// <remarks>
    /// 刻意没有单独的 <c>Initialize</c>：那会让"装配 provider"有两个入口，两者都能建 provider
    /// 却各自维护一半状态。启动即"用加载到的配置装配一次"，与运行时变更是同一操作。
    ///
    /// 实现必须先 flush 旧 provider 再拆除：直接 Dispose 会丢掉缓冲区中尚未导出的 span，
    /// 而切换端点时丢失的可能正是刚记录的错误 span。
    /// </remarks>
    /// <param name="newSettings">合并后的新配置；<c>Enabled=false</c> 表示拆除后不再重建。</param>
    /// <exception cref="System.Exception">
    /// 候选管线无法构造时抛出；实现必须保留当前有效管线，调用方可在后续 apply 重试。
    /// </exception>
    void Reconfigure(TelemetrySettings newSettings);

    /// <summary>
    /// 关闭 OpenTelemetry（应用退出时调用），在 timeout 内尽量导出残留数据。
    /// </summary>
    /// <param name="timeoutMilliseconds">
    /// 等待上限；-1 表示无限等待；
    /// <see cref="NonBlockingShutdownTimeoutMilliseconds"/> 表示不等待。
    /// 注意该上限是<em>每个 provider</em>的：实现串行关闭 tracer 与 meter，
    /// 故调用方感知到的最长阻塞是本值的两倍。
    /// </param>
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
