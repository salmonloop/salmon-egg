using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

/// <summary>
/// 遥测运行态：把「已持久化的用户设置」投影成正在运行的 OpenTelemetry 管线。
///
/// 之所以放在 Domain 而非直接暴露 Infrastructure 的 TelemetryManager：
/// Presentation.Core 不引用 Infrastructure，启动/关闭 workflow 与设置链路只能依赖 Domain 抽象。
/// 与 <see cref="IAppStartupService"/> 同型——由上层表达意图，平台/基础设施承担副作用。
///
/// 单一入口：启动初始化与运行时变更都走 <see cref="ApplyAsync"/>，没有第二个 Initialize
/// 语义。「初始化」就是「从加载到的设置 apply 一次」，因此不存在两套状态 owner。
/// </summary>
public interface ITelemetryRuntime
{
    /// <summary>
    /// 让遥测管线与给定的设置快照一致。
    ///
    /// 语义要求：
    /// - 幂等：与当前生效配置实质相同时必须直接返回，不得重建 provider
    ///   （否则改主题这类无关设置也会连带拆掉 OTLP 管线并触发一次 flush 等待）；
    /// - 先 flush 再换：旧 provider 缓冲区里的 span 必须先导出，否则切换端点会丢数据；
    /// - 失败不抛：遥测是旁路能力，重建失败只应停用遥测，不得让调用方的操作失败；
    /// - 顺序稳定：并发调用按到达顺序收敛，最终状态等于最后一次 apply 的快照。
    /// </summary>
    /// <param name="settings">刚刚成功持久化（或启动时刚加载）的用户设置。</param>
    Task ApplyAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// 进程退出前尽量导出残留数据。
    /// </summary>
    /// <remarks>
    /// 返回 <see cref="Task"/> 不是假异步：底层 SDK 的 Shutdown 是同步阻塞（按 timeout 等待
    /// 导出完成），实现必须把它移出调用线程，否则关闭窗口时会冻结 UI 线程数秒。
    /// </remarks>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
