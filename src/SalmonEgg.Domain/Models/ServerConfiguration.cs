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
        /// Stdio 命令（仅当 Transport=Stdio 时使用）
        /// </summary>
        public string StdioCommand { get; set; } = string.Empty;

        /// <summary>
        /// Stdio 参数（仅当 Transport=Stdio 时使用）
        /// </summary>
        public string StdioArgs { get; set; } = string.Empty;

        /// <summary>
        /// 传输类型
        /// </summary>
        public TransportType Transport { get; set; }

        public string TransportDisplayName =>
            Transport switch
            {
                TransportType.Stdio => "Stdio（本地）",
                TransportType.HttpSse => "HTTP SSE",
                _ => "WebSocket"
            };

        public string TransportGlyph =>
            Transport switch
            {
                TransportType.Stdio => "\uE756", // CommandPrompt
                TransportType.HttpSse => "\uE774", // Cloud
                _ => "\uE704" // Globe
            };

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

        public string EndpointDisplay
        {
            get
            {
                if (Transport == TransportType.Stdio)
                {
                    var command = (StdioCommand ?? string.Empty).Trim();
                    var args = (StdioArgs ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        return string.Empty;
                    }

                    return string.IsNullOrWhiteSpace(args) ? command : $"{command} {args}";
                }

                return ServerUrl ?? string.Empty;
            }
        }

        public string SubtitleDisplay
        {
            get
            {
                var endpoint = EndpointDisplay;
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    return TransportDisplayName;
                }

                return $"{TransportDisplayName} • {endpoint}";
            }
        }
    }
}
