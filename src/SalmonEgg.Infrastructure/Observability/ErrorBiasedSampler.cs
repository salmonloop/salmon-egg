using System.Diagnostics;
using OpenTelemetry.Trace;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// 把「未中签」从 <see cref="SamplingDecision.Drop"/> 降级为
/// <see cref="SamplingDecision.RecordOnly"/> 的采样器，使 span 仍被完整记录但默认不导出；
/// 最终是否导出由 <see cref="ErrorBiasedExportProcessor"/> 在 span 结束时按状态决定。
/// </summary>
/// <remarks>
/// 为什么必须这样绕：采样决策发生在 span **开始**时，而 <c>SamplingParameters</c> 的全部
/// 公开面只有 ParentContext / TraceId / Name / Kind / Tags / Links——**没有任何状态字段**。
/// 那一刻错误尚未发生，因此「出错必留」不可能由采样器直接实现。OTel 为此提供的机制正是
/// <c>RecordOnly</c>：记录但不导出，把导出决定推迟到 <c>BaseProcessor.OnEnd</c>，届时
/// <see cref="Activity.Status"/> 已确定。
///
/// 代价是被降级的 span 仍要付出记录开销（分配 tag / event），只省下了网络导出。对客户端
/// 应用这是正确的取舍：省流量与后端成本是采样的真实目的，而低采样率下丢掉错误 trace 会
/// 直接让线上排障失效（移动端 2% 采样意味着用户报障时 98% 概率没有对应 trace）。
/// </remarks>
internal sealed class ErrorBiasedSampler : Sampler
{
    private readonly Sampler _normalRateSampler;

    public ErrorBiasedSampler(double normalRate)
    {
        _normalRateSampler = new TraceIdRatioBasedSampler(normalRate);
        Description = $"ErrorBiasedSampler{{normalRate={normalRate}}}";
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        var result = _normalRateSampler.ShouldSample(in samplingParameters);

        // 中签者保持原样（含其 Attributes / TraceStateString，不可重建丢弃）。
        return result.Decision == SamplingDecision.RecordAndSample
            ? result
            : new SamplingResult(SamplingDecision.RecordOnly);
    }
}

/// <summary>
/// 恒返回 <see cref="SamplingDecision.RecordOnly"/>。
/// </summary>
/// <remarks>
/// 用于 <see cref="ParentBasedSampler"/> 的 parent-not-sampled 分支。这不是可选的润色：
/// <c>ParentBasedSampler</c> 的单参构造把 localParentNotSampled / remoteParentNotSampled
/// 默认成 <c>AlwaysOffSampler</c>，而本方案下父 span 常态是 RecordOnly（未 Recorded），
/// 于是子 span 会**根本不被创建**（实测连 OnEnd 都不触发），error-biased 就只对 root span
/// 生效、内层 ACP 请求的错误永久丢失。必须显式把这两个分支也设为 RecordOnly，让整条链都
/// 保持「已记录、待裁决」状态。
/// </remarks>
internal sealed class RecordOnlySampler : Sampler
{
    public RecordOnlySampler() => Description = "RecordOnlySampler";

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        => new(SamplingDecision.RecordOnly);
}
