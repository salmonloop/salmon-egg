using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using UnoAcpClient.Application.Services;
using UnoAcpClient.Application.UseCases;
using UnoAcpClient.Application.Validators;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Domain.Services;
using UnoAcpClient.Infrastructure.Logging;
using UnoAcpClient.Infrastructure.Network;
using UnoAcpClient.Infrastructure.Serialization;
using UnoAcpClient.Infrastructure.Storage;
using UnoAcpClient.Presentation.ViewModels;

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

        // 连接管理器（使用工厂方法支持动态传输选择）
        services.AddSingleton<IConnectionManager>(sp =>
        {
            var protocolService = sp.GetRequiredService<IAcpProtocolService>();
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            ITransport TransportFactory(TransportType type)
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
        services.AddSingleton<IValidator<ServerConfiguration>, ServerConfigurationValidator>();
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
    }

    private static void RegisterViewModels(IServiceCollection services)
    {
        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ConfigurationEditorViewModel>();
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
