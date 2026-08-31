namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// 关闭进度的写入面，只应注入给 teardown 的单一 owner。
/// </summary>
/// <remarks>
/// 之所以与只读的 <see cref="IApplicationShutdownProgress"/> 分开：关闭进度必须只有一个写入者
/// （<see cref="IApplicationShutdownWorkflow"/> 的实现）。若读写同在一个接口上，任何拿到它做
/// 投影的 View 或 ViewModel 都能顺手回写，"正在关闭"就有了第二套状态源——这正是本仓库
/// 「单一状态链路」约束要禁止的形态。
/// </remarks>
public interface IApplicationShutdownProgressSink
{
    /// <summary>
    /// 记录清理已进入某一阶段。
    /// </summary>
    void ReportPhase(ApplicationShutdownPhase phase);
}
