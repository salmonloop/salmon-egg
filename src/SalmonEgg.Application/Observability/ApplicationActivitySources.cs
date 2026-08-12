using System.Diagnostics;

namespace SalmonEgg.Application.Observability;

/// <summary>
/// Application 层拥有的 ActivitySource。
///
/// 归属规则：一个 source 只由**实现该逻辑的那一层**定义，禁止多层定义同名 source
/// （同名会在不同程序集各建一个实例，导致“谁在发射”不可判定，注册与埋点也会错配）。
/// 因此本类只包含实现位于 SalmonEgg.Application 的组件；SessionManager / Transport /
/// Storage / AcpClient 的实现都在 Infrastructure，由那一层的
/// <c>SalmonEggActivitySources</c> 拥有。
/// </summary>
public static class ApplicationActivitySources
{
    /// <summary>
    /// ChatService / ChatServiceFactory（实现位于本层）。
    /// </summary>
    public static readonly ActivitySource ChatService = new(ChatServiceName, "1.0.0");

    /// <summary>
    /// source 名称常量，供 Infrastructure 装配 TracerProvider 时注册，
    /// 不必为拿名字而实例化 source。
    /// </summary>
    public const string ChatServiceName = "SalmonEgg.Application.ChatService";
}
