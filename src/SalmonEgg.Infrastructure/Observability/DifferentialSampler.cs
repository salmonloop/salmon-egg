using System;
using System.Diagnostics;
using OpenTelemetry.Trace;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// 差异化采样的 head 阶段（span 开始时决策）。
///
/// 关键约束：<see cref="Sampler.ShouldSample"/> 在 span **开始时** 被调用，此时
/// span 的最终状态（Ok/Error）和耗时都还不存在，因此 head 阶段无法实现
/// “异常全采集”。本采样器只负责两件事：
///   1. 保证正常流量按 <see cref="SamplingSettings.NormalRate"/> 有一条基线；
///   2. 其余 span 返回 <see cref="SamplingDecision.RecordOnly"/>，使其仍被记录
///      （tag/status 齐全）但默认不导出，留给
///      <see cref="ErrorAndLatencyPromotionProcessor"/> 在 span 结束时按最终结果提升。
///
/// 代价：未中签的 span 仍会被分配和打 tag（RecordOnly），换来的是错误与慢操作
/// 可以被完整捕获。对桌面/移动客户端这一开销可接受；真正昂贵的导出仍受采样约束。
/// </summary>
public sealed class DifferentialSampler : Sampler
{
    private readonly SamplingSettings _settings;

    public DifferentialSampler(SamplingSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Description = $"SalmonEgg.DifferentialSampler{{normal={settings.NormalRate}}}";
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        var parent = samplingParameters.ParentContext;

        // 1. 父 span 已被采样 → 子 span 必须采样，保持 trace 完整性。
        //    ActivityContext 没有 IsValid；用 TraceId 是否为默认值判断上下文有效性。
        var hasParent = parent.TraceId != default;
        if (hasParent && parent.TraceFlags.HasFlag(ActivityTraceFlags.Recorded))
        {
            return new SamplingResult(SamplingDecision.RecordAndSample);
        }

        // 2. 关键操作走独立（更高的）基线采样率。
        if (IsCriticalOperation(samplingParameters.Name))
        {
            return Decide(_settings.CriticalOperationRate);
        }

        // 3. 普通操作按基线采样率。
        return Decide(_settings.NormalRate);
    }

    private bool IsCriticalOperation(string? name)
    {
        if (string.IsNullOrEmpty(name) || _settings.CriticalOperations.Length == 0)
        {
            return false;
        }

        foreach (var critical in _settings.CriticalOperations)
        {
            if (name.Contains(critical, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 未中签时返回 RecordOnly 而非 Drop，以便 span 结束时仍可按错误/耗时提升导出。
    /// </summary>
    private SamplingResult Decide(double rate)
        => SamplingProbability.IsSampled(rate)
            ? new SamplingResult(SamplingDecision.RecordAndSample)
            : new SamplingResult(SamplingDecision.RecordOnly);
}

/// <summary>
/// 采样概率判定。使用 <see cref="Random.Shared"/>，其本身线程安全，
/// 避免自建 Random 需要额外加锁成为热点。
/// </summary>
internal static class SamplingProbability
{
    public static bool IsSampled(double rate)
    {
        if (rate >= 1.0)
        {
            return true;
        }

        if (rate <= 0.0)
        {
            return false;
        }

        return Random.Shared.NextDouble() < rate;
    }
}
