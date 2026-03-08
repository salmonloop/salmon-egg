using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Logging
{
    /// <summary>
    /// 错误日志实现。
    /// 用于记录、检索和清除错误日志。
    /// </summary>
    public class ErrorLogger : IErrorLogger
    {
        private readonly ConcurrentQueue<ErrorLogEntry> _errorLog = new();
        private readonly object _lock = new();
        private const int MaxLogEntries = 1000; // 最多保留 1000 条日志

        /// <summary>
        /// 记录错误。
        /// </summary>
        /// <param name="error">错误日志条目</param>
        public void LogError(ErrorLogEntry error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            _errorLog.Enqueue(error);

            // 如果日志超过最大数量，移除最旧的条目
            while (_errorLog.Count > MaxLogEntries)
            {
                _errorLog.TryDequeue(out _);
            }
        }

        /// <summary>
        /// 记录错误（便捷方法）。
        /// </summary>
        /// <param name="errorCode">错误代码</param>
        /// <param name="errorMessage">错误消息</param>
        /// <param name="severity">严重程度</param>
        /// <param name="methodName">方法名</param>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="exception">异常对象</param>
        public void LogError(
            string errorCode,
            string errorMessage,
            ErrorSeverity severity,
            string? methodName = null,
            string? sessionId = null,
            Exception? exception = null)
        {
            var entry = new ErrorLogEntry(errorCode, errorMessage, severity, methodName, sessionId, exception);
            LogError(entry);
        }

        /// <summary>
        /// 记录信息日志。
        /// </summary>
        public void LogInfo(string message, string? methodName = null, string? sessionId = null)
        {
            LogError("INFO", message, ErrorSeverity.Info, methodName, sessionId);
        }

        /// <summary>
        /// 记录警告日志。
        /// </summary>
        public void LogWarning(string message, string? methodName = null, string? sessionId = null)
        {
            LogError("WARN", message, ErrorSeverity.Warning, methodName, sessionId);
        }

        /// <summary>
        /// 记录错误日志。
        /// </summary>
        public void LogError(string message, string? methodName = null, string? sessionId = null, Exception? exception = null)
        {
            LogError("ERR", message, ErrorSeverity.Error, methodName, sessionId, exception);
        }

        /// <summary>
        /// 记录严重错误日志。
        /// </summary>
        public void LogCritical(string message, string? methodName = null, string? sessionId = null, Exception? exception = null)
        {
            LogError("CRIT", message, ErrorSeverity.Critical, methodName, sessionId, exception);
        }

        /// <summary>
        /// 获取最近的错误日志。
        /// </summary>
        /// <param name="count">要获取的日志数量</param>
        /// <param name="minSeverity">最低严重程度</param>
        /// <returns>错误日志列表</returns>
        public IEnumerable<ErrorLogEntry> GetRecentErrors(int count = 50, ErrorSeverity minSeverity = ErrorSeverity.Info)
        {
            lock (_lock)
            {
                return _errorLog
                    .Where(e => e.Severity >= minSeverity)
                    .OrderByDescending(e => e.Timestamp)
                    .Take(count)
                    .ToList();
            }
        }

        /// <summary>
        /// 获取所有错误日志。
        /// </summary>
        public IEnumerable<ErrorLogEntry> GetAllErrors(ErrorSeverity minSeverity = ErrorSeverity.Info)
        {
            lock (_lock)
            {
                return _errorLog
                    .Where(e => e.Severity >= minSeverity)
                    .OrderByDescending(e => e.Timestamp)
                    .ToList();
            }
        }

        /// <summary>
        /// 清除所有错误日志。
        /// </summary>
        public void ClearErrors()
        {
            lock (_lock)
            {
                var temp = new ConcurrentQueue<ErrorLogEntry>();
                while (_errorLog.TryDequeue(out _)) { }
            }
        }

        /// <summary>
        /// 清除指定严重程度及以下的错误日志。
        /// </summary>
        /// <param name="maxSeverity">要清除的最高严重程度</param>
        public void ClearErrorsUpToSeverity(ErrorSeverity maxSeverity)
        {
            lock (_lock)
            {
                var remaining = new ConcurrentQueue<ErrorLogEntry>();
                while (_errorLog.TryDequeue(out var entry))
                {
                    if (entry.Severity > maxSeverity)
                    {
                        remaining.Enqueue(entry);
                    }
                }

                while (remaining.TryDequeue(out var entry))
                {
                    _errorLog.Enqueue(entry);
                }
            }
        }

        /// <summary>
        /// 获取错误统计信息。
        /// </summary>
        /// <returns>错误统计字典（按严重程度分类）</returns>
        public Dictionary<ErrorSeverity, int> GetErrorStatistics()
        {
            lock (_lock)
            {
                return new Dictionary<ErrorSeverity, int>
                {
                    { ErrorSeverity.Info, _errorLog.Count(e => e.Severity == ErrorSeverity.Info) },
                    { ErrorSeverity.Warning, _errorLog.Count(e => e.Severity == ErrorSeverity.Warning) },
                    { ErrorSeverity.Error, _errorLog.Count(e => e.Severity == ErrorSeverity.Error) },
                    { ErrorSeverity.Critical, _errorLog.Count(e => e.Severity == ErrorSeverity.Critical) }
                };
            }
        }

        /// <summary>
        /// 获取最近的错误数量。
        /// </summary>
        public int GetErrorCount(ErrorSeverity minSeverity = ErrorSeverity.Info)
        {
            lock (_lock)
            {
                return _errorLog.Count(e => e.Severity >= minSeverity);
            }
        }

        /// <summary>
        /// 检查是否存在严重错误。
        /// </summary>
        public bool HasCriticalErrors()
        {
            lock (_lock)
            {
                return _errorLog.Any(e => e.Severity == ErrorSeverity.Critical);
            }
        }

        /// <summary>
        /// 检查是否存在错误或严重错误。
        /// </summary>
        public bool HasErrorsOrHigher()
        {
            lock (_lock)
            {
                return _errorLog.Any(e => e.Severity >= ErrorSeverity.Error);
            }
        }
    }
}
