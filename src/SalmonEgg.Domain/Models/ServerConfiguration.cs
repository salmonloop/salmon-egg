using System.Collections.Generic;

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
        public List<string> StdioArguments { get; set; } = new();

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
        /// 连接超时（秒）
        /// </summary>
        public int ConnectionTimeout { get; set; } = AcpConnectionTimeoutPolicy.DefaultSeconds;

        public string EndpointDisplay
        {
            get
            {
                if (Transport == TransportType.Stdio)
                {
                    var command = (StdioCommand ?? string.Empty).Trim();
                    var args = StdioCommandLine.FormatArgumentsText(StdioArguments);
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        return string.Empty;
                    }

                    return string.IsNullOrWhiteSpace(args) ? command : $"{command} {args}";
                }

                return ServerUrl ?? string.Empty;
            }
        }
    }
}
