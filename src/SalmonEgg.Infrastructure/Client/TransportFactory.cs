using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Serilog;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
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
    private readonly ITransportSupportPolicy _transportSupportPolicy;
    private readonly IStdioTransportFactory _stdioTransportFactory;

    /// <summary>
    /// 创建 <see cref="TransportFactory"/> 的新实例。
    /// </summary>
    /// <param name="logger">日志记录器实例</param>
    public TransportFactory(
        ILogger logger,
        ITransportSupportPolicy transportSupportPolicy,
        IStdioTransportFactory stdioTransportFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _transportSupportPolicy = transportSupportPolicy ?? throw new ArgumentNullException(nameof(transportSupportPolicy));
        _stdioTransportFactory = stdioTransportFactory ?? throw new ArgumentNullException(nameof(stdioTransportFactory));
    }

    /// <summary>
    /// 根据指定的传输类型创建新的传输实例。
    /// </summary>
    /// <param name="transportType">传输类型（Stdio, WebSocket, StreamableHttp）</param>
    /// <param name="command">命令（仅用于 Stdio 传输）</param>
    /// <param name="arguments">命令行参数（仅用于 Stdio 传输）</param>
    /// <param name="url">连接 URL（用于 WebSocket 和 StreamableHttp 传输）</param>
    /// <returns>新创建的 <see cref="ITransport"/> 实例</returns>
    /// <exception cref="ArgumentException">当传输类型不支持或必要参数缺失时抛出</exception>
    /// <exception cref="NotSupportedException">当指定的传输类型未实现时抛出</exception>
    public SalmonEgg.Domain.Interfaces.Transport.ITransport CreateTransport(
        TransportType transportType,
        string? command = null,
        IReadOnlyList<string>? arguments = null,
        string? url = null)
    {
        _logger.Information("Creating transport instance. TransportType={TransportType}", transportType);
        var connectTimeout = TimeSpan.FromSeconds(AcpConnectionTimeoutPolicy.DefaultSeconds);

        return CreateTransportCore(transportType, command, arguments, url, connectTimeout);
    }

    public SalmonEgg.Domain.Interfaces.Transport.ITransport CreateTransport(ServerConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        _logger.Information("Creating transport instance from configuration. TransportType={TransportType}, ProfileId={ProfileId}", configuration.Transport, configuration.Id);
        var connectTimeout = AcpConnectionTimeoutPolicy.ResolveTimeout(configuration.ConnectionTimeout);

        return CreateTransportCore(
            configuration.Transport,
            configuration.Transport == TransportType.Stdio ? configuration.StdioCommand : null,
            configuration.Transport == TransportType.Stdio ? configuration.StdioArguments : null,
            configuration.Transport == TransportType.Stdio ? null : configuration.ServerUrl,
            connectTimeout,
            configuration.Proxy,
            configuration.Transport == TransportType.Stdio ? configuration.StdioEnvironment : null);
    }

    private SalmonEgg.Domain.Interfaces.Transport.ITransport CreateTransportCore(
        TransportType transportType,
        string? command,
        IReadOnlyList<string>? arguments,
        string? url,
        TimeSpan connectTimeout,
        ProxyConfig? proxy = null,
        IReadOnlyDictionary<string, string>? stdioEnvironment = null)
    {
        _logger.Information("Creating transport instance. TransportType={TransportType}", transportType);

        return transportType switch
        {
            TransportType.Stdio => CreateStdioTransport(command, arguments, stdioEnvironment),
            TransportType.WebSocket => CreateWebSocketTransport(url, connectTimeout, proxy),
            TransportType.StreamableHttp => CreateStreamableHttpTransport(url, connectTimeout, proxy),
            _ => throw new NotSupportedException($"Unsupported transport type: {transportType}.")
        };
    }

    /// <summary>
    /// 创建 Stdio 传输实例。
    /// </summary>
    /// <param name="command">命令</param>
    /// <param name="arguments">命令行参数</param>
    /// <param name="environment">叠加到子进程环境的变量</param>
    /// <returns>Stdio 传输实例</returns>
    /// <exception cref="ArgumentException">当命令为空时抛出</exception>
    private SalmonEgg.Domain.Interfaces.Transport.ITransport CreateStdioTransport(
        string? command,
        IReadOnlyList<string>? arguments,
        IReadOnlyDictionary<string, string>? environment)
    {
        if (!_transportSupportPolicy.IsSupported(TransportType.Stdio))
        {
            throw new NotSupportedException(
                _transportSupportPolicy.GetUnsupportedReason(TransportType.Stdio)
                ?? "Stdio transport is not supported on this platform.");
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Stdio transport requires a command.", nameof(command));
        }

        var argsArray = arguments?.ToArray() ?? Array.Empty<string>();
        _logger.Information("Creating Stdio transport. Command={Command}, ArgsCount={ArgsCount}", command, argsArray.Length);

        return _stdioTransportFactory.Create(command.Trim(), argsArray, Encoding.UTF8, environment);
    }

    /// <summary>
    /// 创建 WebSocket 传输实例。
    /// </summary>
    /// <param name="url">WebSocket URL</param>
    /// <returns>WebSocket 传输实例</returns>
    /// <exception cref="ArgumentException">当 URL 为空或无效时抛出</exception>
    private SalmonEgg.Domain.Interfaces.Transport.ITransport CreateWebSocketTransport(string? url, TimeSpan connectTimeout, ProxyConfig? proxy)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("WebSocket transport requires a URL.", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new ArgumentException($"Invalid WebSocket URL: {url}", nameof(url));
        }

        _logger.Information("Creating WebSocket transport. Url={Url}", url);

        var logger = _logger;
        var inner = new SalmonEgg.Infrastructure.Network.WebSocketTransport(
            logger,
            proxyConfiguration: proxy,
            connectTimeout: connectTimeout);
        return new NetworkTransportAdapter(inner, url.Trim());
    }

    /// <summary>
    /// 创建 Streamable HTTP 传输实例(ACP 官方草案 RFD)。
    /// </summary>
    /// <param name="url">Streamable HTTP 端点 URL</param>
    /// <param name="connectTimeout">握手连接超时</param>
    /// <param name="proxy">代理配置</param>
    /// <returns>Streamable HTTP 传输实例</returns>
    /// <exception cref="ArgumentException">当 URL 为空或无效时抛出</exception>
    private SalmonEgg.Domain.Interfaces.Transport.ITransport CreateStreamableHttpTransport(
        string? url,
        TimeSpan connectTimeout,
        ProxyConfig? proxy)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Streamable HTTP transport requires a URL.", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new ArgumentException($"Invalid Streamable HTTP URL: {url}", nameof(url));
        }

        _logger.Information(
            "Creating Streamable HTTP transport. Url={Url} proxyMode={ProxyMode} timeoutSeconds={TimeoutSeconds}",
            url,
            proxy?.Mode ?? ProxyConfig.DefaultMode,
            connectTimeout.TotalSeconds);

        var inner = new SalmonEgg.Infrastructure.Network.StreamableHttpTransport(
            _logger,
            proxyConfiguration: proxy,
            connectTimeout: connectTimeout);
        return new NetworkTransportAdapter(inner, url.Trim());
    }
}
