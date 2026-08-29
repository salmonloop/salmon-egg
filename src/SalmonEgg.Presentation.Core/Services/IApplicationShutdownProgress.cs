using System.ComponentModel;

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// 关闭进度的 authoritative 状态：进程退出前的清理是否正在进行。
/// </summary>
/// <remarks>
/// 与 <see cref="IShellNavigationRuntimeState"/> 同型——Core 持有事实，View 只投影。
/// 唯一写入者是 <see cref="IApplicationShutdownWorkflow"/> 的实现（teardown 的单一 owner）；
/// 任何 View、ViewModel 或平台宿主都不得回写，否则"正在关闭"会出现第二套状态源。
///
/// 这里只表达<b>事实</b>（在关吗、清到哪一步了），不表达<b>呈现策略</b>（要不要弹遮罩、
/// 多久之后弹）。阈值判断属于呈现层，放在投影 ViewModel 里，因此本接口无需时间抽象，
/// 也就没有依赖挂钟的测试。
/// </remarks>
public interface IApplicationShutdownProgress : INotifyPropertyChanged
{
    /// <summary>
    /// 关闭清理是否正在进行。
    /// </summary>
    /// <remarks>
    /// 一经置位便不再回落：进程正在退出，"关闭又取消了"不是本应用存在的状态。
    /// 让它单调可以使投影层无需处理抖动。
    /// </remarks>
    bool IsShuttingDown { get; }

    /// <summary>
    /// 当前正在进行的清理阶段，供提示文案区分"保存记录"与"关闭 agent 进程"。
    /// </summary>
    ApplicationShutdownPhase Phase { get; }
}

/// <summary>
/// 关闭清理的阶段。顺序与 <see cref="IApplicationShutdownWorkflow"/> 的执行顺序一致。
/// </summary>
public enum ApplicationShutdownPhase
{
    /// <summary>尚未开始关闭。</summary>
    NotStarted = 0,

    /// <summary>正在把未落盘的会话状态写入磁盘。</summary>
    PersistingState = 1,

    /// <summary>正在终止 agent 子进程与终端会话。</summary>
    ClosingChildProcesses = 2,

    /// <summary>清理已完成，进程即将退出。</summary>
    Completed = 3
}
