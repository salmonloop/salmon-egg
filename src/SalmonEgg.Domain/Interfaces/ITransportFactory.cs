using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Interfaces;

/// <summary>
/// 传输层工厂接口。
/// 用于根据传输类型动态创建对应的 <see cref="ITransport"/> 实例。
/// 遵循依赖倒置原则，解耦传输创建逻辑与业务逻辑。
/// </summary>
public interface ITransportFactory
{
    /// <summary>
    /// 根据指定的传输类型创建新的传输实例。
    /// </summary>
    /// <param name="transportType">传输类型（Stdio, WebSocket, HttpSse）</param>
    /// <param name="command">命令（仅用于 Stdio 传输）</param>
    /// <param name="args">命令行参数（仅用于 Stdio 传输）</param>
    /// <param name="url">连接 URL（用于 WebSocket 和 HttpSse 传输）</param>
    /// <returns>新创建的 <see cref="ITransport"/> 实例</returns>
    /// <exception cref="ArgumentException">当传输类型不支持或必要参数缺失时抛出</exception>
    /// <exception cref="NotSupportedException">当指定的传输类型未实现时抛出</exception>
    ITransport CreateTransport(
        TransportType transportType,
        string? command = null,
        string? args = null,
        string? url = null);

    /// <summary>
    /// 创建默认传输实例（通常为 Stdio）。
    /// </summary>
    /// <returns>默认的 <see cref="ITransport"/> 实例</returns>
    ITransport CreateDefaultTransport();
}
