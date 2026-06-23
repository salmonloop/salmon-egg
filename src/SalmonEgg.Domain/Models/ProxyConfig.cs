namespace SalmonEgg.Domain.Models
{
    /// <summary>
    /// 代理配置
    /// </summary>
    public class ProxyConfig
    {
        /// <summary>
        /// 代理模式
        /// </summary>
        public ProxyMode Mode { get; set; } = ProxyMode.None;

        /// <summary>
        /// 代理服务器 URL
        /// </summary>
        public string? ProxyUrl { get; set; }
    }
}
