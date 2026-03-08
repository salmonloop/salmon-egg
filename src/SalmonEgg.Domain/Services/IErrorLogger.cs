using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services
{
    /// <summary>
    /// 错误日志接口。
    /// 用于记录、检索和清除错误日志。
    /// </summary>
    public interface IErrorLogger
    {
        /// <summary>
        /// 记录错误。
        /// </summary>
        /// <param name="error">错误日志条目</param>
        void LogError(ErrorLogEntry error);

        /// <summary>
        /// 获取最近的错误日志。
        /// </summary>
        /// <param name="count">要获取的日志数量</param>
        /// <param name="minSeverity">最低严重程度</param>
        /// <returns>错误日志列表</returns>
        IEnumerable<ErrorLogEntry> GetRecentErrors(int count = 50, ErrorSeverity minSeverity = ErrorSeverity.Info);

        /// <summary>
        /// 清除所有错误日志。
        /// </summary>
        void ClearErrors();

        /// <summary>
        /// 清除指定严重程度及以下的错误日志。
        /// </summary>
        /// <param name="maxSeverity">要清除的最高严重程度</param>
        void ClearErrorsUpToSeverity(ErrorSeverity maxSeverity);

        /// <summary>
        /// 获取错误统计信息。
        /// </summary>
        /// <returns>错误统计字典（按严重程度分类）</returns>
        Dictionary<ErrorSeverity, int> GetErrorStatistics();
    }

    /// <summary>
    /// 错误日志条目类。
    /// </summary>
    public class ErrorLogEntry
    {
        /// <summary>
        /// 错误发生的时间戳。
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 错误代码。
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// 错误消息。
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 发生错误的方法名（如果适用）。
        /// </summary>
        public string? MethodName { get; set; }

        /// <summary>
        /// 请求参数（如果适用）。
        /// </summary>
        public string? RequestParams { get; set; }

        /// <summary>
        /// 堆栈跟踪信息（如果适用）。
        /// </summary>
        public string? StackTrace { get; set; }

        /// <summary>
        /// 相关的会话 ID（如果适用）。
        /// </summary>
        public string? SessionId { get; set; }

        /// <summary>
        /// 错误严重程度。
        /// </summary>
        public ErrorSeverity Severity { get; set; }

        /// <summary>
        /// 创建新的错误日志条目。
        /// </summary>
        public ErrorLogEntry()
        {
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// 创建新的错误日志条目。
        /// </summary>
        /// <param name="errorCode">错误代码</param>
        /// <param name="errorMessage">错误消息</param>
        /// <param name="severity">严重程度</param>
        /// <param name="methodName">方法名</param>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="exception">异常对象（可选）</param>
        public ErrorLogEntry(
            string errorCode,
            string errorMessage,
            ErrorSeverity severity,
            string? methodName = null,
            string? sessionId = null,
            Exception? exception = null)
        {
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            Severity = severity;
            MethodName = methodName;
            SessionId = sessionId;
            Timestamp = DateTime.UtcNow;

            if (exception != null)
            {
                StackTrace = exception.StackTrace;
                if (string.IsNullOrEmpty(RequestParams))
                {
                    RequestParams = exception.Message;
                }
            }
        }

        /// <summary>
        /// 判断错误是否属于警告级别或以上。
        /// </summary>
        public bool IsWarningOrHigher => Severity >= ErrorSeverity.Warning;

        /// <summary>
        /// 判断错误是否属于错误级别或以上。
        /// </summary>
        public bool IsErrorOrHigher => Severity >= ErrorSeverity.Error;

        /// <summary>
        /// 判断错误是否属于严重级别。
        /// </summary>
        public bool IsCritical => Severity == ErrorSeverity.Critical;
    }

    /// <summary>
    /// 错误严重程度枚举。
    /// </summary>
    public enum ErrorSeverity
    {
        /// <summary>
        /// 信息级别（非错误，仅记录信息）。
        /// </summary>
        Info = 0,

        /// <summary>
        /// 警告级别（可能发生问题，但不影响功能）。
        /// </summary>
        Warning = 1,

        /// <summary>
        /// 错误级别（功能受影响，但系统仍可运行）。
        /// </summary>
        Error = 2,

        /// <summary>
        /// 严重级别（系统关键功能失效）。
        /// </summary>
        Critical = 3
    }
}
