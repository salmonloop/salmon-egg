using System.Diagnostics.Metrics;

namespace SalmonEgg.Application.Observability;

/// <summary>
/// Application 层拥有的 Meter 与具体指标。
/// 指标命名遵循 OTel 约定：全小写、点分层级、单位放在 unit 参数而非名称里。
/// </summary>
public static class ApplicationMeters
{
    /// <summary>
    /// meter 名称常量，供 Infrastructure 装配 MeterProvider 时注册。
    /// </summary>
    public const string ChatServiceMeterName = "SalmonEgg.Application.ChatService";

    private static readonly Meter ChatServiceMeter = new(ChatServiceMeterName, "1.0.0");

    /// <summary>
    /// 成功创建 ChatService 的次数。维度：transport type。
    /// </summary>
    public static readonly Counter<long> ChatServiceCreated = ChatServiceMeter.CreateCounter<long>(
        "salmonegg.chat_service.created",
        unit: "{instance}",
        description: "Number of ChatService instances successfully created.");

    /// <summary>
    /// 创建 ChatService 失败的次数。维度：error.type。
    /// </summary>
    public static readonly Counter<long> ChatServiceErrors = ChatServiceMeter.CreateCounter<long>(
        "salmonegg.chat_service.errors",
        unit: "{error}",
        description: "Number of failures while creating a ChatService instance.");

    /// <summary>
    /// 一次 agent 调用（ACP <c>session/prompt</c>）的端到端耗时分布。
    /// </summary>
    /// <remarks>
    /// 单位是**秒**：规范定义 <c>unit: "s"</c>、值类型 double。写成毫秒会让后端按
    /// 「秒」渲染出千倍偏差的图。
    ///
    /// 桶边界取规范为**本指标**明文指定的那一档
    /// （<c>docs/gen-ai/gen-ai-metrics.md</c>）。不能挪用
    /// <c>gen_ai.client.operation.duration</c> 的阶梯（0.01–81.92）：那是单次模型调用
    /// 的量级，而 agent 一次调用常达几十秒到数分钟，用那套阶梯会把绝大多数样本堆进
    /// 最后一个桶，P95/P99 失去分辨率。
    ///
    /// 用 advice（<see cref="InstrumentAdvice{T}"/>）而非在 SDK 侧配 View：advice 随
    /// instrument 定义一起走，任何 MeterProvider 装配都自动生效，不会因为某个宿主忘了
    /// 加 View 而退化成默认桶。
    /// </remarks>
    public static readonly Histogram<double> InvokeAgentDuration = ChatServiceMeter.CreateHistogram<double>(
        ApplicationSemanticConventions.GenAi.InvokeAgentDurationMetric,
        unit: "s",
        description: "The end-to-end duration of a single agent invocation.",
        tags: null,
        advice: new InstrumentAdvice<double>
        {
            HistogramBucketBoundaries = new[]
            {
                0.1, 0.2, 0.4, 0.8, 1.6, 3.2, 6.4, 12.8, 25.6, 51.2, 102.4, 204.8, 409.6
            }
        });
}
