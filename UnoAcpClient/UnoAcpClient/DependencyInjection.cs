using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using UnoAcpClient.Domain.Services;
using UnoAcpClient.Infrastructure.Network;
using UnoAcpClient.Infrastructure.Serialization;
using UnoAcpClient.Infrastructure.Storage;
using UnoAcpClient.Infrastructure.Logging;
using UnoAcpClient.Application.Services;

namespace UnoAcpClient;

/// <summary>
/// 依赖注入容器配置
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 配置所有服务和依赖项
    /// </summary>
    public static IServiceCollection AddUnoAcpClient(this IServiceCollection services)
    {
        // 配置 Serilog 日志
        ConfigureLogging(services);

        // 注册领域服务
        RegisterDomainServices(services);

        // 注册基础设施服务
        RegisterInfrastructureServices(services);

        // 注册应用服务
        RegisterApplicationServices(services);

        // 注册 ViewModels
        RegisterViewModels(services);

        return services;
    }

    private static void ConfigureLogging(IServiceCollection services)
    {
        // 获取平台特定的应用数据路径
        var appDataPath = GetAppDataPath();

        // 配置 Serilog
        var logger = LoggingConfiguration.ConfigureLogging(appDataPath);
        Log.Logger = logger;

        // 添加 Serilog 到 Microsoft.Extensions.Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(logger, dispose: true);
        });

        // 注册 Serilog ILogger
        services.AddSingleton(logger);
    }

    private static void RegisterDomainServices(IServiceCollection services)
    {
        // 领域服务接口将在后续任务中实现
        // services.AddSingleton<IAcpProtocolService, AcpMessageParser>();
        // services.AddSingleton<IConnectionManager, ConnectionManager>();
    }

    private static void RegisterInfrastructureServices(IServiceCollection services)
    {
        // 基础设施服务将在后续任务中实现
        // services.AddSingleton<IConfigurationService, ConfigurationManager>();
        // services.AddTransient<ITransport, WebSocketTransport>();
    }

    private static void RegisterApplicationServices(IServiceCollection services)
    {
        // 应用服务将在后续任务中实现
        // services.AddSingleton<IConnectionService, ConnectionService>();
        // services.AddSingleton<IMessageService, MessageService>();
    }

    private static void RegisterViewModels(IServiceCollection services)
    {
        // ViewModels 将在后续任务中实现
        // services.AddTransient<MainViewModel>();
        // services.AddTransient<SettingsViewModel>();
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
        // WebAssembly 使用浏览器存储，这里返回一个虚拟路径
        return "/local/UnoAcpClient";
#else
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnoAcpClient");
#endif
    }
}
