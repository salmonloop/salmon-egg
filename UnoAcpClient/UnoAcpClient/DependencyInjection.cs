using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using UnoAcpClient.Application.Services;
using UnoAcpClient.Application.Services.Chat;
using UnoAcpClient.Application.UseCases;
using UnoAcpClient.Application.Validators;
using UnoAcpClient.Domain.Interfaces;
using UnoAcpClient.Domain.Interfaces.Transport;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Domain.Services;
using UnoAcpClient.Domain.Services.Security;
using UnoAcpClient.Infrastructure.Logging;
using UnoAcpClient.Infrastructure.Network;
using UnoAcpClient.Infrastructure.Serialization;
using UnoAcpClient.Infrastructure.Services;
using UnoAcpClient.Infrastructure.Services.Security;
using UnoAcpClient.Infrastructure.Storage;
using UnoAcpClient.Infrastructure.Transport;
using UnoAcpClient.Infrastructure.Client;
using UnoAcpClient.Presentation.ViewModels;
using UnoAcpClient.Presentation.ViewModels.Chat;

namespace UnoAcpClient;

/// <summary>
/// 依赖注入容器配置
/// Requirements: 7.5
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 配置所有服务和依赖项
    /// </summary>
    public static IServiceCollection AddUnoAcpClient(this IServiceCollection services)
    {
        ConfigureLogging(services);
        RegisterDomainServices(services);
        RegisterInfrastructureServices(services);
        RegisterApplicationServices(services);
        RegisterViewModels(services);
        return services;
    }

    private static void ConfigureLogging(IServiceCollection services)
    {
        var appDataPath = GetAppDataPath();
        var logger = LoggingConfiguration.ConfigureLogging(appDataPath);
        Log.Logger = logger;
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(logger, dispose: true);
        });
        services.AddSingleton(logger);
    }

    private static void RegisterDomainServices(IServiceCollection services)
    {
        // ACP 协议服务
        services.AddSingleton<IAcpProtocolService, AcpMessageParser>();

        // 消息解析器和验证器
        services.AddSingleton<IMessageParser, MessageParser>();
        services.AddSingleton<IMessageValidator, MessageValidator>();

        // 能力管理器
        services.AddSingleton<ICapabilityManager, Infrastructure.Services.CapabilityManager>();

        // 会话管理器
        services.AddSingleton<ISessionManager, Infrastructure.Services.SessionManager>();

        // 路径验证器
        services.AddSingleton<IPathValidator, Infrastructure.Services.Security.PathValidator>();

        // 权限管理器
        services.AddSingleton<IPermissionManager, Infrastructure.Services.Security.PermissionManager>();

        // 错误日志器
        services.AddSingleton<IErrorLogger, ErrorLogger>();

        // 连接管理器（使用工厂方法支持动态传输选择）
        services.AddSingleton<IConnectionManager>(sp =>
        	{
        				var protocolService = sp.GetRequiredService<IAcpProtocolService>();
        				var logger = sp.GetRequiredService<Serilog.ILogger>();

        				Infrastructure.Network.ITransport TransportFactory(TransportType type)
        				{
        					var l = sp.GetRequiredService<Serilog.ILogger>();
        					return type switch
        					{
        						TransportType.HttpSse => new HttpSseTransport(l),
        						_ => new WebSocketTransport(l)
        					};
        				}

        				return new ConnectionManager(protocolService, logger, TransportFactory);
        			});
    }

    private static void RegisterInfrastructureServices(IServiceCollection services)
    {
        // 安全存储
        services.AddSingleton<ISecureStorage, SecureStorage>();

        // 配置管理器
        services.AddSingleton<IConfigurationService, ConfigurationManager>();

        // Validator
        // Validator
        services.AddSingleton<IValidator<ServerConfiguration>, ServerConfigurationValidator>();

        // 传输层工厂
        services.AddSingleton<UnoAcpClient.Domain.Interfaces.ITransportFactory, TransportFactory>();


        }

    private static void RegisterApplicationServices(IServiceCollection services)
    {
        // 用例
        services.AddTransient<ConnectToServerUseCase>();
        services.AddTransient<DisconnectUseCase>();
        services.AddTransient<SendMessageUseCase>();

        // 应用服务
        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<IMessageService, MessageService>();

        // Chat 服务工厂（支持动态创建）
        services.AddSingleton<ChatServiceFactory>(sp =>
        {
            var transportFactory = sp.GetRequiredService<ITransportFactory>();
            var parser = sp.GetRequiredService<IMessageParser>();
            var validator = sp.GetRequiredService<IMessageValidator>();
            var errorLogger = sp.GetRequiredService<IErrorLogger>();
            var capabilityManager = sp.GetRequiredService<ICapabilityManager>();
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            return new ChatServiceFactory(transportFactory, parser, validator, errorLogger, capabilityManager, logger);
        });

        // Chat 服务（默认实例，使用默认传输）
        services.AddSingleton<IChatService>(sp =>
        {
            var factory = sp.GetRequiredService<ChatServiceFactory>();
            return factory.CreateDefaultChatService();
        });

        // 错误恢复服务
        services.AddSingleton<IErrorRecoveryService>(sp =>
        {
            var chatService = sp.GetRequiredService<IChatService>();
            var pathValidator = sp.GetRequiredService<IPathValidator>();
            var errorLogger = sp.GetRequiredService<IErrorLogger>();
            return new ErrorRecoveryService(chatService, pathValidator, errorLogger);
        });
    }

    private static void RegisterViewModels(IServiceCollection services)
    {
        // 原有 ViewModel
        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ConfigurationEditorViewModel>();

        // 新的 Chat ViewModel（重构后）
        // Must be singleton so connection/session state survives navigation and is shared between Settings and Chat pages.
        services.AddSingleton<ChatViewModel>();
    }

    private static string GetAppDataPath()
    {
#if __ANDROID__
        return Android.App.Application.Context.FilesDir?.AbsolutePath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnoAcpClient");
#elif __IOS__
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "..", "Library", "Application Support", "UnoAcpClient");
#elif __MACOS__
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnoAcpClient");
#elif WINDOWS || WINDOWS_UWP
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnoAcpClient");
#elif __WASM__
        return "/local/UnoAcpClient";
#else
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnoAcpClient");
#endif
    }
}
