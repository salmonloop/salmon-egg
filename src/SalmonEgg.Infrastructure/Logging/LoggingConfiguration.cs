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
    public static ILogger ConfigureLogging(string appDataPath, bool enableDebugMode = true)
    {
        // 确保日志目录存在 (Requirement 6.4)
        var logDirectory = Path.Combine(appDataPath, "logs");
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        var logPath = Path.Combine(logDirectory, "app-.log");

        // 根据调试模式设置日志级别 (Requirement 6.2, 6.3)
        // 默认启用 Verbose 级别以查看完整的传输层调试信息
        var minimumLevel = enableDebugMode ? LogEventLevel.Verbose : LogEventLevel.Information;

        return new LoggerConfiguration()
            // 配置日志级别 (Requirement 6.2)
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Debug)
            .MinimumLevel.Override("System", LogEventLevel.Debug)
            // 添加上下文信息 (Requirement 6.1)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithMachineName()
            // 配置控制台 sink
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            // 配置文件 sink (Requirements 6.1, 6.4, 6.5)
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10_485_760, // 10MB 限制 (Requirement 6.5)
                retainedFileCountLimit: 7,      // 保留 7 天 (Requirement 6.5)
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
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
