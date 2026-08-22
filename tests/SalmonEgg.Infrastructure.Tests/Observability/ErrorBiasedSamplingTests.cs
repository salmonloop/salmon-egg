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
/// error-biased 采样的行为门禁：断言「哪些 span 真的被导出」这一可观察结果，
/// 而非采样器/处理器的内部形态。
/// </summary>
/// <remarks>
/// 采样率一律取 0.0：这不是「关闭采样」，而是把「正常流量未中签」这一条件确定化。
/// 用真实比率会让断言依赖 traceId 随机性而 flaky，且无法区分「错误被救回」与「碰巧中签」。
/// 反向验证记录：把 <c>ErrorBiasedSampler</c> 换回裸 <c>TraceIdRatioBasedSampler</c>，
/// ErrorSpan_IsExported* 会因导出集为空而失败；移除 <c>ErrorBiasedExportProcessor</c> 亦同。
/// 把 ParentBasedSampler 改回单参构造，则 NestedErrorUnderNonSampledParent_* 会因子 span
/// 根本不被创建而失败。
/// </remarks>
public sealed class ErrorBiasedSamplingTests
{
    private const string SourceName = "SalmonEgg.Tests.ErrorBiasedSampling";

    [Fact]
    public void SuccessfulSpan_IsNotExported_WhenNormalRateExcludesIt()
    {
        using var harness = new SamplingHarness(normalRate: 0.0);

        harness.EmitSpan("ok", ActivityStatusCode.Ok);

        Assert.Empty(harness.ExportedSpanNames);
    }

    [Fact]
    public void ErrorSpan_IsExported_EvenWhenNormalRateExcludesIt()
    {
        using var harness = new SamplingHarness(normalRate: 0.0);

        harness.EmitSpan("failed", ActivityStatusCode.Error);

        Assert.Equal(["failed"], harness.ExportedSpanNames);
    }

    [Fact]
    public void ErrorSpan_IsExportedAlongsideDroppedSuccesses()
    {
        using var harness = new SamplingHarness(normalRate: 0.0);

        harness.EmitSpan("ok-1", ActivityStatusCode.Ok);
        harness.EmitSpan("failed", ActivityStatusCode.Error);
        harness.EmitSpan("ok-2", ActivityStatusCode.Ok);

        // 只有错误 span 穿过采样，成功的两个被丢弃：这正是 error-biased 的用户可观察效果。
        Assert.Equal(["failed"], harness.ExportedSpanNames);
    }

    [Fact]
    public void UnsetStatusSpan_IsNotExported()
    {
        using var harness = new SamplingHarness(normalRate: 0.0);

        // 取消属于正常路径（ChatService 对调用方取消刻意不标 Error），不应因此产生导出。
        harness.EmitSpan("cancelled", ActivityStatusCode.Unset);

        Assert.Empty(harness.ExportedSpanNames);
    }

    [Fact]
    public void NestedErrorUnderNonSampledParent_IsStillRecordedAndExported()
    {
        using var harness = new SamplingHarness(normalRate: 0.0);

        // 父 span 处于 RecordOnly。若 parent-not-sampled 分支落到 AlwaysOff，
        // 子 span 会根本不被创建，内层 ACP 错误将永久丢失。
        using (var parent = harness.Source.StartActivity("parent"))
        {
            Assert.NotNull(parent);
            using (var child = harness.Source.StartActivity("child"))
            {
                Assert.NotNull(child);
                child.SetStatus(ActivityStatusCode.Error, "inner failure");
            }

            parent.SetStatus(ActivityStatusCode.Ok);
        }

        harness.Flush();
        Assert.Equal(["child"], harness.ExportedSpanNames);
    }

    [Fact]
    public void ErrorPropagatedThroughCallStack_ExportsWholeChainWithoutOrphans()
    {
        using var harness = new SamplingHarness(normalRate: 0.0);

        // 现有代码的真实形态：每层 catch 各自 SetStatus(Error) 后 rethrow。
        try
        {
            using var parent = harness.Source.StartActivity("chat.session.prompt");
            try
            {
                using var child = harness.Source.StartActivity("acp.request");
                try
                {
                    throw new InvalidOperationException("rpc failure");
                }
                catch (Exception ex)
                {
                    child?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }
            }
            catch (Exception ex)
            {
                parent?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }
        catch (InvalidOperationException)
        {
            // 已在两级 span 上记录，向上冒泡到此为止。
        }

        harness.Flush();

        // 两级都被独立提升，导出的是完整链：错误 trace 在后端可从 root 一路下钻。
        Assert.Equal(2, harness.ExportedSpans.Count);
        Assert.Single(harness.ExportedSpans, span => span.ParentSpanId == default);
        var root = harness.ExportedSpans.Single(span => span.ParentSpanId == default);
        var inner = harness.ExportedSpans.Single(span => span.ParentSpanId != default);
        Assert.Equal(root.SpanId, inner.ParentSpanId);
    }

    private sealed class SamplingHarness : IDisposable
    {
        private readonly TracerProvider _provider;
        private readonly RecordingExporter _exporter = new();

        public SamplingHarness(double normalRate)
        {
            Source = new ActivitySource(SourceName, "1.0.0");
            _provider = Sdk.CreateTracerProviderBuilder()
                .AddSource(SourceName)
                .SetSampler(new ParentBasedSampler(
                    rootSampler: new ErrorBiasedSampler(normalRate),
                    remoteParentSampled: new AlwaysOnSampler(),
                    remoteParentNotSampled: new RecordOnlySampler(),
                    localParentSampled: new AlwaysOnSampler(),
                    localParentNotSampled: new RecordOnlySampler()))
                .AddProcessor(new ErrorBiasedExportProcessor())
                .AddProcessor(new SimpleActivityExportProcessor(_exporter))
                .Build();
        }

        public ActivitySource Source { get; }

        public IReadOnlyList<Activity> ExportedSpans => _exporter.Exported;

        public IReadOnlyList<string> ExportedSpanNames
            => _exporter.Exported.Select(static span => span.DisplayName).ToList();

        public void EmitSpan(string name, ActivityStatusCode status)
        {
            using (var activity = Source.StartActivity(name))
            {
                // span 必须真的被创建，否则「未导出」会是假绿（根本没产生数据）。
                Assert.NotNull(activity);
                if (status != ActivityStatusCode.Unset)
                {
                    activity.SetStatus(status);
                }
            }

            Flush();
        }

        public void Flush() => _provider.ForceFlush(5000);

        public void Dispose()
        {
            _provider.Dispose();
            Source.Dispose();
        }
    }

    private sealed class RecordingExporter : BaseExporter<Activity>
    {
        private readonly List<Activity> _exported = [];

        public IReadOnlyList<Activity> Exported => _exported;

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                _exported.Add(activity);
            }

            return ExportResult.Success;
        }
    }
}
