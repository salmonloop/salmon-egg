using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Interfaces.Transport
{
    /// <summary>
    /// 传输层接口。
    /// 定义了与 Agent 通信的底层传输方法。
    /// </summary>
    public interface ITransport
    {
        /// <summary>
        /// 消息接收事件。当收到消息时触发。
        /// </summary>
        event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// 传输错误事件。当发生传输错误时触发。
        /// </summary>
        event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// 判断传输是否已连接。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 建立与 Agent 的连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否成功连接</returns>
        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开与 Agent 的连接。
        /// </summary>
        /// <returns>是否成功断开</returns>
        Task<bool> DisconnectAsync();

        /// <summary>
        /// 发送消息。
        /// </summary>
        /// <param name="message">要发送的 JSON 消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否成功发送</returns>
        Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 消息接收事件参数。
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// 接收到的 JSON 消息字符串。
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 消息接收时间。
        /// </summary>
        public DateTime ReceivedAt { get; set; }

        /// <summary>
        /// 创建新的消息接收事件参数。
        /// </summary>
        public MessageReceivedEventArgs()
        {
            ReceivedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 创建新的消息接收事件参数。
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <param name="receivedAt">接收时间</param>
        public MessageReceivedEventArgs(string message, DateTime? receivedAt = null)
        {
            Message = message;
            ReceivedAt = receivedAt ?? DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 传输错误事件参数。
    /// </summary>
    public class TransportErrorEventArgs : EventArgs
    {
        /// <summary>
        /// 发生的异常（如果有的话）。
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// 错误消息。
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 错误发生时间。
        /// </summary>
        public DateTime ErrorTime { get; set; }

        /// <summary>
        /// 创建新的传输错误事件参数。
        /// </summary>
        public TransportErrorEventArgs()
        {
            ErrorTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 创建新的传输错误事件参数。
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        /// <param name="exception">异常对象</param>
        public TransportErrorEventArgs(string errorMessage, Exception? exception = null)
        {
            ErrorMessage = errorMessage;
            Exception = exception;
            ErrorTime = DateTime.UtcNow;
        }
    }
}
