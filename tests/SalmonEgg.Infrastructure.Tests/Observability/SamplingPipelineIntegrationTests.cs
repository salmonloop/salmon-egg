using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenTelemetry;
using OpenTelemetry.Trace;
using SalmonEgg.Infrastructure.Observability;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Observability;

/// <summary>
/// 端到端验证采样管线：真实 OpenTelemetry SDK + DifferentialSampler +
/// ErrorAndLatencyPromotionProcessor 一起工作时，哪些 span 最终真正被导出。
///
/// 这一层不可由单测替代：单测只能分别断言 sampler 返回 RecordOnly、processor 会置
/// Recorded 位；而“错误 100% 落地”是二者协作 + processor 注册顺序 + SDK 导出判定
/// 三方共同决定的结果，只有把 span 跑过真实 pipeline 才能证明。
/// </summary>
public class SamplingPipelineIntegrationTests : IDisposable
{
    private const string SourceName = "SalmonEgg.Infrastructure.Transport";

    private readonly ActivitySource _source = new(SourceName);

    public void Dispose() => _source.Dispose();

    /// <summary>
    /// 用 NormalRate=0 构建 pipeline：head 阶段必然不中签，
    /// 因此任何被导出的 span 都只能来自 tail 阶段的提升，判定不受随机性干扰。
    /// </summary>
    private static TracerProvider BuildProvider(
        List<Activity> exported,
        SamplingSettings settings)
        => Sdk.CreateTracerProviderBuilder()
            .AddSource(SourceName)
            .SetSampler(new DifferentialSampler(settings))
            // 顺序与生产代码 TelemetryManager 一致：提升 processor 必须在导出器之前。
            // 反向验证记录：移除本行会使 ErrorSpan / SpanWithExceptionEvent / SlowSpan
            // 三个用例失败（collection was empty），证明该门禁真实有效。
            .AddProcessor(new ErrorAndLatencyPromotionProcessor(settings))
            .AddProcessor(new SimpleActivityExportProcessor(new CollectingExporter(exported)))
            .Build();

    private static SamplingSettings NeverSampleHead(
        double errorRate = 1.0,
        long slowThresholdMs = 3000,
        double slowRate = 1.0,
        long verySlowThresholdMs = 10000,
        double verySlowRate = 1.0)
        => new()
        {
            NormalRate = 0.0,
            CriticalOperationRate = 0.0,
            CriticalOperations = Array.Empty<string>(),
            ErrorRate = errorRate,
            SlowOperationThresholdMs = slowThresholdMs,
            SlowOperationRate = slowRate,
            VerySlowOperationThresholdMs = verySlowThresholdMs,
            VerySlowOperationRate = verySlowRate,
        };

    [Fact]
    public void ErrorSpan_IsExported_EvenWhenHeadSamplingRejects()
    {
        var exported = new List<Activity>();
        using var provider = BuildProvider(exported, NeverSampleHead());

        using (var activity = _source.StartActivity("failing-op"))
        {
            Assert.NotNull(activity);
            activity!.SetStatus(ActivityStatusCode.Error, "boom");
        }

        provider.ForceFlush();

        // 这是差异化采样的核心承诺：正常流量采样率为 0，错误依然 100% 落地。
        var span = Assert.Single(exported);
        Assert.Equal("failing-op", span.DisplayName);
    }

    [Fact]
    public void SpanWithExceptionEvent_IsExported_EvenWithoutErrorStatus()
    {
        var exported = new List<Activity>();
        using var provider = BuildProvider(exported, NeverSampleHead());

        using (var activity = _source.StartActivity("throwing-op"))
        {
            Assert.NotNull(activity);
            // 只记录 exception event、不设 Error status —— 部分库就是这种写法，
            // 若 processor 仅看 Status 就会漏掉这类 span。
            activity!.AddEvent(new ActivityEvent("exception"));
        }

        provider.ForceFlush();

        Assert.Single(exported);
    }

    [Fact]
    public void SuccessfulFastSpan_IsNotExported_WhenHeadSamplingRejects()
    {
        var exported = new List<Activity>();
        using var provider = BuildProvider(exported, NeverSampleHead());

        using (var activity = _source.StartActivity("fast-ok-op"))
        {
            Assert.NotNull(activity);
            activity!.SetStatus(ActivityStatusCode.Ok);
        }

        provider.ForceFlush();

        // 反向断言：证明前两个用例的导出确实来自提升逻辑，而不是 pipeline 无脑放行一切。
        Assert.Empty(exported);
    }

    [Fact]
    public void SlowSpan_IsExported_WhenExceedingSlowThreshold()
    {
        var exported = new List<Activity>();
        // 阈值设为 0，使任何正常结束的 span 都算“慢”，避免测试依赖真实等待时间。
        using var provider = BuildProvider(
            exported,
            NeverSampleHead(slowThresholdMs: 0, slowRate: 1.0, verySlowThresholdMs: long.MaxValue));

        using (var activity = _source.StartActivity("slow-op"))
        {
            Assert.NotNull(activity);
            activity!.SetStatus(ActivityStatusCode.Ok);
        }

        provider.ForceFlush();

        Assert.Single(exported);
    }

    [Fact]
    public void ChildSpan_IsExported_WhenParentWasSampled()
    {
        var exported = new List<Activity>();
        // ErrorRate=0 让提升逻辑对本用例完全失效，从而单独验证父子传播这一条路径。
        using var provider = BuildProvider(
            exported,
            new SamplingSettings
            {
                NormalRate = 1.0,          // 父 span 必中签
                ErrorRate = 0.0,
                SlowOperationRate = 0.0,
                VerySlowOperationRate = 0.0,
                CriticalOperations = Array.Empty<string>(),
                CriticalOperationRate = 0.0,
            });

        using (var parent = _source.StartActivity("parent-op"))
        {
            Assert.NotNull(parent);
            using var child = _source.StartActivity("child-op");
            Assert.NotNull(child);
        }

        provider.ForceFlush();

        // trace 完整性：父被采样时子必须一起导出，否则 trace 会出现断链。
        Assert.Equal(2, exported.Count);
        Assert.Contains(exported, a => a.DisplayName == "parent-op");
        Assert.Contains(exported, a => a.DisplayName == "child-op");
    }

    private sealed class CollectingExporter(List<Activity> collected) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                collected.Add(activity);
            }

            return ExportResult.Success;
        }
    }
}
