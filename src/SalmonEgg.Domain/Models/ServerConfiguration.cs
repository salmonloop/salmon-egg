namespace SalmonEgg.Domain.Models
{
    /// <summary>
    /// 服务器配置
    /// </summary>
    public class ServerConfiguration
    {
        /// <summary>
        /// 配置唯一标识符
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 配置名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 服务器 URL
        /// </summary>
        public string ServerUrl { get; set; } = string.Empty;

        /// <summary>
        /// 传输类型
        /// </summary>
        public TransportType Transport { get; set; }

        /// <summary>
        /// 认证配置
        /// </summary>
        public AuthenticationConfig? Authentication { get; set; }

        /// <summary>
        /// 代理配置
        /// </summary>
        public ProxyConfig? Proxy { get; set; }

        /// <summary>
        /// 心跳间隔（秒）
        /// </summary>
        public int HeartbeatInterval { get; set; } = 30;

        /// <summary>
        /// 连接超时（秒）
        /// </summary>
        public int ConnectionTimeout { get; set; } = 10;
    }
}
