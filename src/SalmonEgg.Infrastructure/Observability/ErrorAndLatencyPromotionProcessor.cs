using System;
using System.Diagnostics;
using OpenTelemetry;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// 差异化采样的 tail 阶段（span 结束时决策）。
///
/// <see cref="DifferentialSampler"/> 在 span 开始时把未中签的 span 置为
/// <c>RecordOnly</c>：会被记录但 <see cref="Activity.Recorded"/> 为 false，
/// 因而 SDK 的批量导出 processor 会直接跳过它。本 processor 在 <see cref="OnEnd"/>
/// 检查 span 的**最终**状态与耗时，对满足条件的 span 置上
/// <see cref="ActivityTraceFlags.Recorded"/>，把它提升为可导出。
///
/// 这是“错误 100% 采集、慢操作高采样”唯一正确的落点——这些事实在 span 开始时
/// 并不存在，Sampler 无法看到。
///
/// 注册顺序有硬要求：本 processor 必须早于导出 processor 加入 pipeline，
/// 否则提升发生在导出判定之后，不生效（见 <see cref="TelemetryManager"/>）。
/// </summary>
public sealed class ErrorAndLatencyPromotionProcessor : BaseProcessor<Activity>
{
    private readonly SamplingSettings _settings;

    public ErrorAndLatencyPromotionProcessor(SamplingSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public override void OnEnd(Activity data)
    {
        if (data == null)
        {
            return;
        }

        // head 阶段已中签，无需处理。
        if (data.Recorded)
        {
            return;
        }

        var rate = ResolvePromotionRate(data);
        if (rate <= 0.0)
        {
            return;
        }

        if (SamplingProbability.IsSampled(rate))
        {
            // 置上 Recorded 位，使后续导出 processor 接受该 span。
            data.ActivityTraceFlags |= ActivityTraceFlags.Recorded;
        }
    }

    /// <summary>
    /// 按优先级解析该 span 应使用的提升采样率：
    /// 错误 &gt; 非常慢 &gt; 慢 &gt; 不提升。
    /// </summary>
    private double ResolvePromotionRate(Activity data)
    {
        if (IsError(data))
        {
            return _settings.ErrorRate;
        }

        var elapsedMs = data.Duration.TotalMilliseconds;

        if (elapsedMs >= _settings.VerySlowOperationThresholdMs)
        {
            return _settings.VerySlowOperationRate;
        }

        if (elapsedMs >= _settings.SlowOperationThresholdMs)
        {
            return _settings.SlowOperationRate;
        }

        return 0.0;
    }

    /// <summary>
    /// 错误判定同时覆盖三种写法：显式 <see cref="ActivityStatusCode.Error"/>、
    /// 由 OTel 约定 tag 写入的 <c>otel.status_code=ERROR</c>（部分库只写 tag
    /// 不设 status），以及记录过 exception event。
    /// </summary>
    private static bool IsError(Activity data)
    {
        if (data.Status == ActivityStatusCode.Error)
        {
            return true;
        }

        foreach (var tag in data.TagObjects)
        {
            if (string.Equals(tag.Key, "otel.status_code", StringComparison.Ordinal)
                && string.Equals(tag.Value?.ToString(), "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var activityEvent in data.Events)
        {
            if (string.Equals(activityEvent.Name, "exception", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
