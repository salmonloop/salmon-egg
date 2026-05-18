using System;
using System.IO;
using Serilog;
using Serilog.Events;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Logging;

/// <summary>
/// Serilog 日志配置
/// Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 7.4
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// 配置 Serilog 日志系统
    /// </summary>
    /// <param name="appDataPath">应用数据目录路径（平台特定）</param>
    /// <param name="enableDebugMode">是否启用调试模式（记录详细的网络通信）</param>
    /// <returns>配置好的 ILogger 实例</returns>
    public static ILogger ConfigureLogging(
        string appDataPath,
        bool enableDebugMode = false,
        LoggingHostCapabilities? hostCapabilities = null)
    {
        hostCapabilities ??= LoggingHostCapabilities.Desktop;

        string? logPath = null;
        if (hostCapabilities.Value.SupportsFileSink)
        {
            // 确保日志目录存在 (Requirement 6.4)
            var logDirectory = Path.Combine(appDataPath, "logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            logPath = Path.Combine(logDirectory, "app-.log");
        }

        var minimumLevel = enableDebugMode ? LogEventLevel.Verbose : LogEventLevel.Information;

        var configuration = new LoggerConfiguration()
            // 配置日志级别 (Requirement 6.2)
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", enableDebugMode ? LogEventLevel.Debug : LogEventLevel.Warning)
            .MinimumLevel.Override("System", enableDebugMode ? LogEventLevel.Debug : LogEventLevel.Warning)
            // 添加上下文信息 (Requirement 6.1)
            .Enrich.FromLogContext();

        if (hostCapabilities.Value.SupportsThreadEnricher)
        {
            configuration = configuration.Enrich.WithThreadId();
        }

        if (hostCapabilities.Value.SupportsMachineNameEnricher)
        {
            configuration = configuration.Enrich.WithMachineName();
        }

        if (hostCapabilities.Value.SupportsConsoleSink)
        {
            configuration = configuration.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        }

        if (logPath != null)
        {
            // 配置文件 sink (Requirements 6.1, 6.4, 6.5)
            configuration = configuration.WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10_485_760, // 10MB 限制 (Requirement 6.5)
                retainedFileCountLimit: 7,      // 保留 7 天 (Requirement 6.5)
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}");
        }

        return configuration.CreateLogger();
    }

    /// <summary>
    /// 获取平台特定的应用数据目录路径
    /// </summary>
    /// <returns>平台特定的应用数据目录路径</returns>
    public static string GetPlatformSpecificLogPath()
    {
        return SalmonEggPaths.GetAppDataRootPath();
    }
}

public readonly record struct LoggingHostCapabilities(
    bool SupportsConsoleSink,
    bool SupportsFileSink,
    bool SupportsThreadEnricher,
    bool SupportsMachineNameEnricher)
{
    public static LoggingHostCapabilities Desktop { get; } = new(
        SupportsConsoleSink: true,
        SupportsFileSink: true,
        SupportsThreadEnricher: true,
        SupportsMachineNameEnricher: true);

    public static LoggingHostCapabilities BrowserWebAssembly { get; } = new(
        SupportsConsoleSink: false,
        SupportsFileSink: false,
        SupportsThreadEnricher: false,
        SupportsMachineNameEnricher: false);
}
