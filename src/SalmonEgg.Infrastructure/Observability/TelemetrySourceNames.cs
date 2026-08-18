using SalmonEgg.Acp.Observability;
using SalmonEgg.Application.Observability;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// TracerProvider / MeterProvider 需要注册的 source 名称清单。
///
/// 集中在此处的原因：<see cref="TelemetryManager"/> 必须显式 <c>AddSource</c> /
/// <c>AddMeter</c> 每一个名称，漏一个该层的埋点就会静默丢失（不报错、无告警）。
/// 名称一律引用各层公开的常量，避免此处与埋点处各写一份字符串而漂移。
///
/// 注：OTel .NET 的 <c>AddSource</c> 支持 <c>"SalmonEgg.*"</c> 通配，但通配会把未来
/// 任何以此为前缀的第三方 source 一并纳入，故这里保持显式枚举。
/// </summary>
internal static class TelemetrySourceNames
{
    public static readonly string[] ActivitySources =
    [
        ApplicationActivitySources.ChatServiceName,
        AcpActivitySources.ClientName
    ];

    public static readonly string[] Meters =
    [
        ApplicationMeters.ChatServiceMeterName
    ];
}
