using System;
using System.Collections.Generic;
using System.Diagnostics;
using Serilog;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Acp.Client;
using SalmonEgg.Application.Services.Acp;
using SalmonEgg.Application.Observability;

namespace SalmonEgg.Application.Services.Chat;

/// <summary>
/// Chat 服务工厂。
/// 用于根据传输配置动态创建新的 <see cref="IChatService"/> 实例。
/// 封装了从传输配置到完整 Chat 服务链的创建逻辑。
/// </summary>
public class ChatServiceFactory
{
    private readonly ITransportFactory _transportFactory;
    private readonly IErrorLogger _errorLogger;
    private readonly ISessionManager _sessionManager;
    private readonly IAcpClientFactory _acpClientFactory;
    private readonly ILogger _logger;
    private readonly Func<IChatService, IChatService> _decorateChatService;

    /// <summary>
    /// 创建 <see cref="ChatServiceFactory"/> 的新实例。
    /// </summary>
    /// <param name="transportFactory">传输层工厂</param>
    /// <param name="errorLogger">错误日志器</param>
    /// <param name="logger">日志记录器</param>
    public ChatServiceFactory(
        ITransportFactory transportFactory,
        IErrorLogger errorLogger,
        ISessionManager sessionManager,
        IAcpClientFactory acpClientFactory,
        ILogger logger,
        Func<IChatService, IChatService>? decorateChatService = null)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _errorLogger = errorLogger ?? throw new ArgumentNullException(nameof(errorLogger));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _acpClientFactory = acpClientFactory ?? throw new ArgumentNullException(nameof(acpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _decorateChatService = decorateChatService ?? (service => service);
    }

    /// <summary>
    /// 根据传输配置创建新的 <see cref="IChatService"/> 实例。
    /// </summary>
    /// <param name="transportType">传输类型</param>
    /// <param name="command">命令（仅用于 Stdio）</param>
    /// <param name="arguments">命令行参数（仅用于 Stdio）</param>
    /// <param name="url">连接 URL（用于 WebSocket 和 StreamableHttp）</param>
    /// <returns>新创建的 <see cref="IChatService"/> 实例</returns>
    /// <exception cref="InvalidOperationException">当必要参数缺失时抛出</exception>
    public IChatService CreateChatService(
        TransportType transportType,
        string? command = null,
        IReadOnlyList<string>? arguments = null,
        string? url = null)
    {
        using var activity = ApplicationActivitySources.ChatService.StartActivity(
            "chat.service.create",
            ActivityKind.Internal);

        activity?.SetTag(ApplicationSemanticConventions.Chat.TransportType, transportType.ToString());

        try
        {
            _logger?.Information("Creating ChatService instance. TransportType={TransportType}", transportType);

            // 1. 创建传输层
            var transport = _transportFactory.CreateTransport(transportType, command, arguments, url);

            // 2. 创建 ACP 客户端
            var acpClient = _acpClientFactory.CreateClient(transport);

            // 3. 创建 Chat 服务
            var chatService = _decorateChatService(new ChatService(acpClient, _errorLogger, _sessionManager));

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag(ApplicationSemanticConventions.Chat.ServiceType, chatService.GetType().Name);

            // 记录 Metrics
            ApplicationMeters.ChatServiceCreated.Add(1, new KeyValuePair<string, object?>(
                ApplicationSemanticConventions.Chat.TransportType, transportType.ToString()));

            return chatService;
        }
        catch (Exception ex)
        {
            // SetErrorStatus 写 span 级 error.type（低基数分类）；
            // RecordException 写 exception.* event（异常明细）。两者分工不同，都需要。
            activity?.SetErrorStatus(ex);
            activity?.RecordException(ex);

            // 记录错误 Metrics。维度用类型全名，与 span 上的 error.type 取值保持一致。
            ApplicationMeters.ChatServiceErrors.Add(1, new KeyValuePair<string, object?>(
                OtelErrorAttributes.Type, ex.GetType().FullName));

            _logger?.Error(ex, "Failed to create chat service for {TransportType}", transportType);
            throw;
        }
    }

    public IChatService CreateChatService(ServerConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        using var activity = ApplicationActivitySources.ChatService.StartActivity(
            "chat.service.create_from_configuration",
            ActivityKind.Internal);

        activity?.SetTag(ApplicationSemanticConventions.Chat.TransportType, configuration.Transport.ToString());

        try
        {
            _logger?.Information("Creating ChatService instance from configuration. TransportType={TransportType}",
                configuration.Transport);

            var transport = _transportFactory.CreateTransport(configuration);
            var acpClient = _acpClientFactory.CreateClient(transport);
            var chatService = _decorateChatService(new ChatService(acpClient, _errorLogger, _sessionManager));

            activity?.SetStatus(ActivityStatusCode.Ok);

            // 记录 Metrics
            ApplicationMeters.ChatServiceCreated.Add(1,
                new KeyValuePair<string, object?>(ApplicationSemanticConventions.Chat.TransportType, configuration.Transport.ToString()));

            return chatService;
        }
        catch (Exception ex)
        {
            activity?.SetErrorStatus(ex);
            activity?.RecordException(ex);

            ApplicationMeters.ChatServiceErrors.Add(1,
                new KeyValuePair<string, object?>(OtelErrorAttributes.Type, ex.GetType().FullName));

            _logger?.Error(ex, "Failed to create chat service from configuration for {TransportType}", configuration.Transport);
            throw;
        }
    }
}
