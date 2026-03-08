using System;
using Serilog;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Infrastructure.Network;
using SalmonEgg.Infrastructure.Transport;

namespace SalmonEgg.Infrastructure.Client;

/// <summary>
/// 传输层工厂实现。
/// 根据指定的传输类型创建对应的 <see cref="ITransport"/> 实例。
/// 封装了传输创建的复杂性，提供统一的创建接口。
/// </summary>
public class TransportFactory : ITransportFactory
{
   private readonly ILogger _logger;

   /// <summary>
   /// 创建 <see cref="TransportFactory"/> 的新实例。
   /// </summary>
   /// <param name="logger">日志记录器实例</param>
   public TransportFactory(ILogger logger)
   {
       _logger = logger ?? throw new ArgumentNullException(nameof(logger));
   }

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
   public SalmonEgg.Domain.Interfaces.Transport.ITransport CreateTransport(
       TransportType transportType,
       string? command = null,
       string? args = null,
       string? url = null)
   {
       _logger?.Information("正在创建传输实例：{TransportType}", transportType);

       return transportType switch
       {
           TransportType.Stdio => CreateStdioTransport(command, args),
           TransportType.WebSocket => CreateWebSocketTransport(url),
           TransportType.HttpSse => CreateHttpSseTransport(url),
           _ => throw new NotSupportedException($"不支持的传输类型：{transportType}")
       };
   }

   /// <summary>
   /// 创建默认传输实例（Stdio）。
   /// </summary>
   /// <returns>默认的 <see cref="ITransport"/> 实例</returns>
   public SalmonEgg.Domain.Interfaces.Transport.ITransport CreateDefaultTransport()
   {
       _logger?.Information("创建默认传输实例：Stdio");
       // 默认使用 Stdio 传输，参数为空的命令
       return new StdioTransport("agent-command", Array.Empty<string>(), System.Text.Encoding.UTF8);
   }

   /// <summary>
   /// 创建 Stdio 传输实例。
   /// </summary>
   /// <param name="command">命令</param>
   /// <param name="args">命令行参数</param>
   /// <returns>Stdio 传输实例</returns>
   /// <exception cref="ArgumentException">当命令为空时抛出</exception>
   private SalmonEgg.Domain.Interfaces.Transport.ITransport CreateStdioTransport(string? command, string? args)
   {
       if (string.IsNullOrWhiteSpace(command))
       {
           throw new ArgumentException("Stdio 传输必须指定命令", nameof(command));
       }

       _logger?.Information("创建 Stdio 传输：Command={Command}, Args={Args}", command, args);

       var argsArray = string.IsNullOrWhiteSpace(args)
           ? Array.Empty<string>()
           : args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

       return new StdioTransport(command.Trim(), argsArray, System.Text.Encoding.UTF8);
   }

   /// <summary>
   /// 创建 WebSocket 传输实例。
   /// </summary>
   /// <param name="url">WebSocket URL</param>
   /// <returns>WebSocket 传输实例</returns>
   /// <exception cref="ArgumentException">当 URL 为空或无效时抛出</exception>
   private SalmonEgg.Domain.Interfaces.Transport.ITransport CreateWebSocketTransport(string? url)
   {
       if (string.IsNullOrWhiteSpace(url))
       {
           throw new ArgumentException("WebSocket 传输必须指定 URL", nameof(url));
       }

       if (!Uri.TryCreate(url, UriKind.Absolute, out _))
       {
           throw new ArgumentException($"无效的 WebSocket URL: {url}", nameof(url));
       }

       _logger?.Information("创建 WebSocket 传输：Url={Url}", url);

       // TODO: WebSocketTransport 需要实现 Domain.Interfaces.Transport.ITransport
       // 当前实现使用 Infrastructure.Network.ITransport，需要重构或创建适配器
       throw new NotImplementedException("WebSocket 传输需要重构 WebSocketTransport 类以支持 Domain.Interfaces.Transport.ITransport");
       // var transport = new WebSocketTransport(url.Trim(), _logger);
       // return transport;
   }

   /// <summary>
   /// 创建 HTTP SSE 传输实例。
   /// </summary>
   /// <param name="url">HTTP SSE URL</param>
   /// <returns>HTTP SSE 传输实例</returns>
   /// <exception cref="ArgumentException">当 URL 为空或无效时抛出</exception>
   private SalmonEgg.Domain.Interfaces.Transport.ITransport CreateHttpSseTransport(string? url)
   {
       if (string.IsNullOrWhiteSpace(url))
       {
           throw new ArgumentException("HTTP SSE 传输必须指定 URL", nameof(url));
       }

       if (!Uri.TryCreate(url, UriKind.Absolute, out _))
       {
           throw new ArgumentException($"无效的 HTTP SSE URL: {url}", nameof(url));
       }

       _logger?.Information("创建 HTTP SSE 传输：Url={Url}", url);

       // TODO: HttpSseTransport 需要实现 Domain.Interfaces.Transport.ITransport
       // 当前实现使用 Infrastructure.Network.ITransport，需要重构或创建适配器
       throw new NotImplementedException("HTTP SSE 传输需要重构 HttpSseTransport 类以支持 Domain.Interfaces.Transport.ITransport");
       // var transport = new HttpSseTransport(url.Trim(), _logger);
       // return transport;
   }
}
