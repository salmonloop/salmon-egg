using System;

namespace SalmonEgg.Domain.Models
{
    /// <summary>
    /// 连接状态模型
    /// </summary>
    public class ConnectionState
    {
        /// <summary>
        /// 连接状态
        /// </summary>
        public ConnectionStatus Status { get; set; }

        /// <summary>
        /// 服务器 URL
        /// </summary>
        public string ServerUrl { get; set; } = string.Empty;

        /// <summary>
        /// 连接时间
        /// </summary>
        public DateTime? ConnectedAt { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// 延迟
        /// </summary>
        public TimeSpan? Latency { get; set; }
    }
}
