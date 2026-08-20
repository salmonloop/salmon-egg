using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace SalmonEgg.Infrastructure.Logging;

/// <summary>
/// 把当前 <see cref="Activity"/> 的 trace 上下文写入日志事件，使日志与 trace 可以互相关联。
///
/// 属性名采用 OpenTelemetry 约定的 <c>TraceId</c> / <c>SpanId</c>：OTLP 日志记录里
/// 这两个字段是顶层字段，采集端（Collector / Loki / Seq）普遍按此名做 trace-log 关联。
///
/// 实现上直接读 <see cref="Activity.Current"/> 而非依赖 Serilog 的 OpenTelemetry sink：
/// 这样即使未配置 OTLP 导出（例如仅写本地文件的离线排查场景），文件日志里同样带有
/// 关联 ID，不会出现“开了 OTLP 才有 trace 关联”的割裂。
/// </summary>
public sealed class TraceContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            // 不在任何 span 内（例如启动阶段日志）——不写空属性，避免产生
            // TraceId="" 这类噪声字段干扰下游查询。
            return;
        }

        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("TraceId", activity.TraceId.ToHexString()));
        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("SpanId", activity.SpanId.ToHexString()));
    }
}
