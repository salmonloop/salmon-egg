using System;
using Serilog;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Client;
namespace SalmonEgg.Application.Services.Chat;

/// <summary>
/// Chat 服务工厂。
/// 用于根据传输配置动态创建新的 <see cref="IChatService"/> 实例。
/// 封装了从传输配置到完整 Chat 服务链的创建逻辑。
/// </summary>
public class ChatServiceFactory
{
    private readonly ITransportFactory _transportFactory;
    private readonly IMessageParser _messageParser;
    private readonly IMessageValidator _messageValidator;
    private readonly IErrorLogger _errorLogger;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger _logger;
    private readonly Func<IChatService, IChatService> _decorateChatService;

    /// <summary>
    /// 创建 <see cref="ChatServiceFactory"/> 的新实例。
    /// </summary>
    /// <param name="transportFactory">传输层工厂</param>
    /// <param name="messageParser">消息解析器</param>
    /// <param name="messageValidator">消息验证器</param>
    /// <param name="errorLogger">错误日志器</param>
    /// <param name="logger">日志记录器</param>
    public ChatServiceFactory(
        ITransportFactory transportFactory,
        IMessageParser messageParser,
        IMessageValidator messageValidator,
        IErrorLogger errorLogger,
        ISessionManager sessionManager,
        ILogger logger,
        Func<IChatService, IChatService>? decorateChatService = null)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
        _messageValidator = messageValidator ?? throw new ArgumentNullException(nameof(messageValidator));
        _errorLogger = errorLogger ?? throw new ArgumentNullException(nameof(errorLogger));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _decorateChatService = decorateChatService ?? (service => service);
    }

    /// <summary>
    /// 根据传输配置创建新的 <see cref="IChatService"/> 实例。
    /// </summary>
    /// <param name="transportType">传输类型</param>
    /// <param name="command">命令（仅用于 Stdio）</param>
    /// <param name="args">命令行参数（仅用于 Stdio）</param>
    /// <param name="url">连接 URL（用于 WebSocket 和 HttpSse）</param>
    /// <returns>新创建的 <see cref="IChatService"/> 实例</returns>
    /// <exception cref="InvalidOperationException">当必要参数缺失时抛出</exception>
    public IChatService CreateChatService(
        TransportType transportType,
        string? command = null,
        string? args = null,
        string? url = null)
    {
        _logger?.Information("正在创建新的 ChatService 实例：TransportType={TransportType}", transportType);

        // 1. 创建传输层
        var transport = _transportFactory.CreateTransport(transportType, command, args, url);

        // 2. 创建 ACP 客户端
        var acpClient = new AcpClient(
            transport,
            _messageParser,
            _messageValidator);

        // 3. 创建 Chat 服务
        return _decorateChatService(new ChatService(acpClient, _errorLogger, _sessionManager));
    }

    /// <summary>
    /// 创建默认的 <see cref="IChatService"/> 实例（使用 Stdio 传输）。
    /// </summary>
    /// <returns>默认的 <see cref="IChatService"/> 实例</returns>
    public IChatService CreateDefaultChatService()
    {
        _logger?.Information("创建默认 ChatService 实例：Stdio 传输");
        return CreateChatService(TransportType.Stdio, "agent-command", null, null);
    }
}
