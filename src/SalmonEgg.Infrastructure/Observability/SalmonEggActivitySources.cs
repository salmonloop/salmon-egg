using System.Diagnostics;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// Infrastructure 层拥有的 ActivitySource。
///
/// 归属规则见 <c>SalmonEgg.Application.Observability.ApplicationActivitySources</c>：
/// 一个 source 只由实现该逻辑的那一层定义。ChatService 的实现位于 Application 层，
/// 故本类不再定义同名 source。
/// </summary>
public static class SalmonEggActivitySources
{
    public const string SessionManagerName = "SalmonEgg.Infrastructure.SessionManager";
    public const string AcpClientName = "SalmonEgg.Infrastructure.AcpClient";
    public const string TransportName = "SalmonEgg.Infrastructure.Transport";
    public const string StorageName = "SalmonEgg.Infrastructure.Storage";

    /// <summary>会话生命周期（创建 / 加载 / 保存 / 删除）。</summary>
    public static readonly ActivitySource SessionManager = new(SessionManagerName, "1.0.0");

    /// <summary>ACP 协议请求（JSON-RPC 往返）。</summary>
    public static readonly ActivitySource AcpClient = new(AcpClientName, "1.0.0");

    /// <summary>传输层连接与收发（stdio / websocket / http）。</summary>
    public static readonly ActivitySource Transport = new(TransportName, "1.0.0");

    /// <summary>本地存储读写。</summary>
    public static readonly ActivitySource Storage = new(StorageName, "1.0.0");
}
