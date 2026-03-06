using System;

namespace UnoAcpClient.Domain.Exceptions
{
    /// <summary>
    /// 连接相关异常
    /// </summary>
    public class ConnectionException : Exception
    {
        /// <summary>
        /// 错误类型
        /// </summary>
        public ConnectionErrorType ErrorType { get; set; }
        
        /// <summary>
        /// 服务器 URL
        /// </summary>
        public string ServerUrl { get; set; }
        
        /// <summary>
        /// 创建连接异常实例
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="errorType">错误类型</param>
        public ConnectionException(string message, ConnectionErrorType errorType) 
            : base(message)
        {
            ErrorType = errorType;
        }
        
        /// <summary>
        /// 创建连接异常实例（包含服务器 URL）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="errorType">错误类型</param>
        /// <param name="serverUrl">服务器 URL</param>
        public ConnectionException(string message, ConnectionErrorType errorType, string serverUrl) 
            : base(message)
        {
            ErrorType = errorType;
            ServerUrl = serverUrl;
        }
        
        /// <summary>
        /// 创建连接异常实例（包含内部异常）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="errorType">错误类型</param>
        /// <param name="innerException">内部异常</param>
        public ConnectionException(string message, ConnectionErrorType errorType, Exception innerException) 
            : base(message, innerException)
        {
            ErrorType = errorType;
        }
        
        /// <summary>
        /// 创建连接异常实例（包含服务器 URL 和内部异常）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="errorType">错误类型</param>
        /// <param name="serverUrl">服务器 URL</param>
        /// <param name="innerException">内部异常</param>
        public ConnectionException(string message, ConnectionErrorType errorType, string serverUrl, Exception innerException) 
            : base(message, innerException)
        {
            ErrorType = errorType;
            ServerUrl = serverUrl;
        }
    }
}
