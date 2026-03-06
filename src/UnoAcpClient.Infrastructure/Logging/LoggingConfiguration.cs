using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace UnoAcpClient.Infrastructure.Logging;

/// <summary>
/// Serilog 日志配置
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// 配置 Serilog 日志系统
    /// </summary>
    /// <param name="appDataPath">应用数据目录路径</param>
    /// <returns>配置好的 ILogger 实例</returns>
    public static ILogger ConfigureLogging(string appDataPath)
    {
        // 确保日志目录存在
        var logDirectory = Path.Combine(appDataPath, "logs");
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        var logPath = Path.Combine(logDirectory, "app-.log");

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10_485_760, // 10MB
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
