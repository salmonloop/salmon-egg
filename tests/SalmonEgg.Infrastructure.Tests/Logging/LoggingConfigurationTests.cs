using System;
using System.IO;
using Serilog;
using Serilog.Events;
using SalmonEgg.Infrastructure.Logging;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Logging;

/// <summary>
/// 日志配置测试
/// </summary>
public class LoggingConfigurationTests
{
    [Fact]
    public void ConfigureLogging_ShouldCreateLogger()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), "SalmonEggTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            // Act
            var logger = LoggingConfiguration.ConfigureLogging(tempPath);

            // Assert
            Assert.NotNull(logger);
            
            // 关闭 logger
            (logger as IDisposable)?.Dispose();
        }
        finally
        {
            // Cleanup
            System.Threading.Thread.Sleep(100);
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, true); } catch { }
            }
        }
    }

    [Fact]
    public void ConfigureLogging_ShouldCreateLogDirectory()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), "SalmonEggTests", Guid.NewGuid().ToString());

        try
        {
            // Act
            var logger = LoggingConfiguration.ConfigureLogging(tempPath);

            // Assert
            var logDirectory = Path.Combine(tempPath, "logs");
            Assert.True(Directory.Exists(logDirectory), "日志目录应该被创建");
            
            // 关闭 logger
            (logger as IDisposable)?.Dispose();
        }
        finally
        {
            // Cleanup
            System.Threading.Thread.Sleep(100);
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, true); } catch { }
            }
        }
    }

    [Fact]
    public void ConfigureLogging_WithDebugMode_ShouldEnableDebugLevel()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), "SalmonEggTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            // Act
            var logger = LoggingConfiguration.ConfigureLogging(tempPath, enableDebugMode: true);

            // Assert
            Assert.NotNull(logger);
            // 验证 Debug 级别的日志可以被记录
            logger.Debug("Test debug message");
            
            // 关闭 logger 以释放文件句柄
            (logger as IDisposable)?.Dispose();
        }
        finally
        {
            // Cleanup - 等待一下确保文件句柄被释放
            System.Threading.Thread.Sleep(100);
            if (Directory.Exists(tempPath))
            {
                try
                {
                    Directory.Delete(tempPath, true);
                }
                catch
                {
                    // 忽略清理错误
                }
            }
        }
    }

    [Fact]
    public void ConfigureLogging_ShouldWriteToLogFile()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), "SalmonEggTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            // Act
            var logger = LoggingConfiguration.ConfigureLogging(tempPath);
            logger.Information("Test log message");
            
            // 关闭 logger 以确保日志被写入
            (logger as IDisposable)?.Dispose();

            // Assert
            var logDirectory = Path.Combine(tempPath, "logs");
            var logFiles = Directory.GetFiles(logDirectory, "app-*.log");
            Assert.NotEmpty(logFiles);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ConfigureLogging_LogFileShouldContainTimestampAndLevel()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), "SalmonEggTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            // Act
            var logger = LoggingConfiguration.ConfigureLogging(tempPath);
            var testMessage = $"Test message {Guid.NewGuid()}";
            logger.Information(testMessage);
            
            // 关闭 logger 以确保日志被写入
            (logger as IDisposable)?.Dispose();

            // Assert
            var logDirectory = Path.Combine(tempPath, "logs");
            var logFiles = Directory.GetFiles(logDirectory, "app-*.log");
            Assert.NotEmpty(logFiles);

            var logContent = File.ReadAllText(logFiles[0]);
            Assert.Contains(testMessage, logContent);
            Assert.Contains("[INF]", logContent); // 日志级别
            // 验证时间戳格式 (yyyy-MM-dd HH:mm:ss.fff)
            Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}", logContent);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ConfigureLogging_ShouldIncludeThreadIdInLogFile()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), "SalmonEggTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            // Act
            var logger = LoggingConfiguration.ConfigureLogging(tempPath);
            logger.Information("Test message with thread ID");
            
            // 关闭 logger 以确保日志被写入
            (logger as IDisposable)?.Dispose();

            // Assert
            var logDirectory = Path.Combine(tempPath, "logs");
            var logFiles = Directory.GetFiles(logDirectory, "app-*.log");
            Assert.NotEmpty(logFiles);

            var logContent = File.ReadAllText(logFiles[0]);
            Assert.Contains("[Thread:", logContent); // 验证包含线程 ID
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void GetPlatformSpecificLogPath_ShouldReturnValidPath()
    {
        // Act
        var path = LoggingConfiguration.GetPlatformSpecificLogPath();

        // Assert
        Assert.NotNull(path);
        Assert.NotEmpty(path);
        Assert.Contains("SalmonEgg", path);
    }
}
