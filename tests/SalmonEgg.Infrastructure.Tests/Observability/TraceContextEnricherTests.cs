using System;
using System.Diagnostics;
using System.Linq;
using SalmonEgg.Infrastructure.Logging;
using Serilog;
using Serilog.Events;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Observability;

public class TraceContextEnricherTests
{
    [Fact]
    public void Enrich_InsideActivity_AddsTraceAndSpanIds()
    {
        // Arrange: 必须注册 listener，否则 StartActivity 返回 null（采样器未订阅时无 Activity）
        using var listener = CreateAlwaysOnListener("TraceContextEnricherTests");
        using var source = new ActivitySource("TraceContextEnricherTests");
        var events = new List<LogEvent>();
        using var logger = new LoggerConfiguration()
            .Enrich.With(new TraceContextEnricher())
            .WriteTo.Sink(new CollectingSink(events))
            .CreateLogger();

        // Act
        using var activity = source.StartActivity("op");
        Assert.NotNull(activity);
        logger.Information("inside span");

        // Assert: 属性值必须等于当前 activity 的实际 ID，而非只断言"存在"
        var logEvent = Assert.Single(events);
        Assert.Equal(
            activity!.TraceId.ToHexString(),
            logEvent.Properties["TraceId"].ToString().Trim('"'));
        Assert.Equal(
            activity.SpanId.ToHexString(),
            logEvent.Properties["SpanId"].ToString().Trim('"'));
    }

    [Fact]
    public void Enrich_WithoutActivity_DoesNotAddEmptyProperties()
    {
        // Arrange
        var events = new List<LogEvent>();
        using var logger = new LoggerConfiguration()
            .Enrich.With(new TraceContextEnricher())
            .WriteTo.Sink(new CollectingSink(events))
            .CreateLogger();

        // 防御测试间串扰：前一个用例若泄漏 Activity.Current 会让本用例假绿
        Activity.Current = null;

        // Act
        logger.Information("no span");

        // Assert: 不写空属性，避免下游查询被 TraceId="" 噪声污染
        var logEvent = Assert.Single(events);
        Assert.False(logEvent.Properties.ContainsKey("TraceId"));
        Assert.False(logEvent.Properties.ContainsKey("SpanId"));
    }

    [Fact]
    public void Enrich_NestedActivity_UsesInnermostSpanId()
    {
        // Arrange
        using var listener = CreateAlwaysOnListener("TraceContextEnricherTests.Nested");
        using var source = new ActivitySource("TraceContextEnricherTests.Nested");
        var events = new List<LogEvent>();
        using var logger = new LoggerConfiguration()
            .Enrich.With(new TraceContextEnricher())
            .WriteTo.Sink(new CollectingSink(events))
            .CreateLogger();

        // Act
        using var outer = source.StartActivity("outer");
        using var inner = source.StartActivity("inner");

        logger.Information("in nested span");

        // Assert: 同一 trace 下应取最内层 span，否则日志会被归到错误的 span
        Assert.NotNull(inner);
        var logEvent = Assert.Single(events);
        Assert.Equal(
            inner!.SpanId.ToHexString(),
            logEvent.Properties["SpanId"].ToString().Trim('"'));
        Assert.Equal(
            outer!.TraceId.ToHexString(),
            logEvent.Properties["TraceId"].ToString().Trim('"'));
    }

    private static ActivityListener CreateAlwaysOnListener(string sourceName)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class CollectingSink(List<LogEvent> events) : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
