using System.Diagnostics;
using OpenTelemetry;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// 在 span 结束时把出错的 span 提升为「可导出」，使低采样率下错误 trace 不被丢弃。
/// 与 <see cref="ErrorBiasedSampler"/> 配对：采样器负责「记录但不导出」，本处理器负责
/// 在状态已知后做真正的导出裁决。
/// </summary>
/// <remarks>
/// 注册顺序有硬要求：本处理器必须排在导出处理器**之前**。OTel 按注册顺序串成链，
/// 导出处理器读的是它被调用那一刻的 <see cref="ActivityTraceFlags.Recorded"/>；排在后面
/// 提升 flag 就晚了，错误 span 依旧不会被导出（而且不报错，只是静默丢失）。
///
/// 关于孤儿 span：OnEnd 的实际顺序是**子先父后**，且子 span 的整条处理器链（含导出）会在
/// 父 span 开始处理之前跑完，SDK 没有 trace 级缓冲，因此「等整条 trace 结束再统一裁决」
/// 做不到——不要为此设计跨 span 的协调状态。实践中这不构成问题：异常沿调用栈冒泡时，每层
/// catch 各自 <c>SetErrorStatus</c> 后 rethrow，于是每一级都独立被提升，导出的是完整的错误
/// 链。只有「子出错但父吞掉异常并判成功」时才会出现单独的错误子 span，而那种情况父确实不
/// 该算失败，后端仍可按 traceId 把它聚合到所属 trace 下。
/// </remarks>
internal sealed class ErrorBiasedExportProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (data is null)
        {
            return;
        }

        if (data.Status == ActivityStatusCode.Error)
        {
            data.ActivityTraceFlags |= ActivityTraceFlags.Recorded;
        }
    }
}
