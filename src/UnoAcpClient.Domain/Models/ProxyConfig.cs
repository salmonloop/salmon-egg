namespace UnoAcpClient.Domain.Models
{
    /// <summary>
    /// 代理配置
    /// </summary>
    public class ProxyConfig
    {
        /// <summary>
        /// 是否启用代理
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 代理服务器 URL
        /// </summary>
        public string? ProxyUrl { get; set; }
    }
}
