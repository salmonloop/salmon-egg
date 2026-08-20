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
}
