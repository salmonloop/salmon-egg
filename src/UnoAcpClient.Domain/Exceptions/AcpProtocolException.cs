using System;

namespace UnoAcpClient.Domain.Exceptions
{
    /// <summary>
    /// ACP 协议相关异常
    /// </summary>
    public class AcpProtocolException : Exception
    {
        /// <summary>
        /// 消息 ID
        /// </summary>
        public string MessageId { get; set; }
        
        /// <summary>
        /// 错误代码
        /// </summary>
        public AcpErrorCode ErrorCode { get; set; }
        
        /// <summary>
        /// 创建 ACP 协议异常实例
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="errorCode">错误代码</param>
        public AcpProtocolException(string message, AcpErrorCode errorCode) 
            : base(message)
        {
            ErrorCode = errorCode;
        }
        
        /// <summary>
        /// 创建 ACP 协议异常实例（包含消息 ID）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="errorCode">错误代码</param>
        /// <param name="messageId">消息 ID</param>
        public AcpProtocolException(string message, AcpErrorCode errorCode, string messageId) 
            : base(message)
        {
            ErrorCode = errorCode;
            MessageId = messageId;
        }
        
        /// <summary>
        /// 创建 ACP 协议异常实例（包含内部异常）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="errorCode">错误代码</param>
        /// <param name="innerException">内部异常</param>
        public AcpProtocolException(string message, AcpErrorCode errorCode, Exception innerException) 
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }
}
