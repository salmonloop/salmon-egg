using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SalmonEgg.Application.Services;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Application.UseCases;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;
using SalmonEgg.Infrastructure.Logging;
using SalmonEgg.Infrastructure.Network;
using SalmonEgg.Infrastructure.Serialization;
using SalmonEgg.Infrastructure.Services;
using SalmonEgg.Infrastructure.Services.Security;
using SalmonEgg.Infrastructure.Storage;
using SalmonEgg.Infrastructure.Transport;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg;

/// <summary>
/// 依赖注入容器配置
/// Requirements: 7.5
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 配置所有服务和依赖项
    /// </summary>
    public static IServiceCollection AddSalmonEgg(this IServiceCollection services)
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

        // App settings (config/app.yaml)
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IAppDataService, AppDataService>();
        services.AddSingleton<IAppMaintenanceService, AppMaintenanceService>();

        // 配置管理器
        services.AddSingleton<IConfigurationService, ConfigurationManager>();

        // Validator
        // Validator
        services.AddSingleton<IValidator<ServerConfiguration>, ServerConfigurationValidator>();

        // 传输层工厂
        services.AddSingleton<SalmonEgg.Domain.Interfaces.ITransportFactory, TransportFactory>();


        // Diagnostics & platform shell
        services.AddSingleton<IDiagnosticsBundleService, SalmonEgg.Infrastructure.Services.DiagnosticsBundleService>();
        services.AddSingleton<IPlatformShellService, SalmonEgg.Infrastructure.Services.PlatformShellService>();

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
            var sessionManager = sp.GetRequiredService<ISessionManager>();
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            return new ChatServiceFactory(transportFactory, parser, validator, errorLogger, capabilityManager, sessionManager, logger);
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
        services.AddTransient<ConfigurationEditorViewModel>();

        // 新的 Chat ViewModel（重构后）
        // Must be singleton so connection/session state survives navigation and is shared between Settings and Chat pages.
        services.AddSingleton<ChatViewModel>();

        // Sidebar navigation state (projects/sessions)
        services.AddSingleton<SidebarViewModel>();

        // App preferences used by General/Appearance settings and window behaviors.
        services.AddSingleton<AppPreferencesViewModel>();

        // ACP connection profiles (server presets)
        services.AddSingleton<AcpProfilesViewModel>();

        // ACP connection settings page view model (wraps Chat + Profiles)
        services.AddSingleton<AcpConnectionSettingsViewModel>();

        // Settings pages (Data/Shortcuts/Diagnostics/About)
        services.AddSingleton<DataStorageSettingsViewModel>();
        services.AddSingleton<ShortcutsSettingsViewModel>();
        services.AddSingleton<DiagnosticsSettingsViewModel>();
        services.AddSingleton<AboutViewModel>();

        // Shell navigation facade (prevents Settings pages from walking the visual tree)
        services.AddSingleton<IShellNavigationService, ShellNavigationService>();
    }

    private static string GetAppDataPath()
    {
        return SalmonEggPaths.GetAppDataRootPath();
    }
}
