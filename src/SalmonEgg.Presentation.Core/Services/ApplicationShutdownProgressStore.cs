using CommunityToolkit.Mvvm.ComponentModel;

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// <see cref="IApplicationShutdownProgress"/> 的实现，同时向 teardown owner 暴露写入面。
/// </summary>
/// <remarks>
/// 读写分离成两个接口，是为了让"谁能写"在类型上就是显式的：View 与投影 ViewModel 只拿到
/// <see cref="IApplicationShutdownProgress"/>，写入面 <see cref="IApplicationShutdownProgressSink"/>
/// 只注入给 <see cref="ApplicationShutdownWorkflow"/>。这样第二套写入者无法被顺手加进来。
///
/// 与 <see cref="ShellNavigationRuntimeStateStore"/> 同型：Core 持有事实，用
/// <c>ObservableObject</c> 通知投影层。
/// </remarks>
public sealed partial class ApplicationShutdownProgressStore
    : ObservableObject, IApplicationShutdownProgress, IApplicationShutdownProgressSink
{
    [ObservableProperty]
    private bool _isShuttingDown;

    [ObservableProperty]
    private ApplicationShutdownPhase _phase = ApplicationShutdownPhase.NotStarted;

    public void ReportPhase(ApplicationShutdownPhase phase)
    {
        // 单调置位：一旦进入关闭就不再回落到"未开始"，投影层因此无需处理抖动。
        if (phase != ApplicationShutdownPhase.NotStarted)
        {
            IsShuttingDown = true;
        }

        Phase = phase;
    }
}
